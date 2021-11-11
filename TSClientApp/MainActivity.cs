using Android;
using Android.App;
using Android.Content.PM;
using Android.OS;
using Android.Util;
using Android.Views;
using Android.Widget;
using AndroidX.AppCompat.App;
using AndroidX.Core.App;
using Google.Android.Material.FloatingActionButton;
using Google.Android.Material.Snackbar;
using Nethereum.Signer;
using Nethereum.Web3;
using Nethereum.Web3.Accounts;
using Plugin.BLE.Abstractions.Contracts;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using Xamarin.Essentials;
using Nethereum.ABI.FunctionEncoding.Attributes;
using Nethereum.Contracts;
using System.Numerics;
using Java.Util;

namespace App1
{
    public delegate void ReceivedMessage(byte[] message);
    public delegate void RestartServer();

    [Activity(Label = "@string/app_name", Theme = "@style/AppTheme.NoActionBar", MainLauncher = true)]
    public class MainActivity : AppCompatActivity
    {
        private string lockAddr = "<YOUR LOCK BLUETOOTH ADDR>"; //eg 125623562356 - 'bluetoothAddress' from keyfile, with colons removed
        private string key = "<handshake key>";  // 'handshakeKey' from keyfile, should be 32 hex characters    
        private int keyOffset = 1; // 'handshakeKeyIndex' from keyfile, usually is 1
        private Plugin.Android.AugustLock.AugustLockDevice august;

        private string[] challengeWords = { "Apples", "Oranges", "Grapes", "DragonFruit", "BreadFruit", "Pomegranate", "Aubergine", "Fungi", "Falafel", "Carrot", "Tomato"};
        private string currentChallenge = "";

        protected override void OnCreate(Bundle savedInstanceState)
        {
            base.OnCreate(savedInstanceState);
            Xamarin.Essentials.Platform.Init(this, savedInstanceState);
            SetContentView(Resource.Layout.activity_main);

            layout = FindViewById<LinearLayout>(Resource.Id.status_layout);//

            AndroidX.AppCompat.Widget.Toolbar toolbar = FindViewById<AndroidX.AppCompat.Widget.Toolbar>(Resource.Id.toolbar);
            SetSupportActionBar(toolbar);

            fab = FindViewById<FloatingActionButton>(Resource.Id.fab);
            fab.Click += FabOnClick;

            GetLocationPermission();

            //scan for lock
            august = new Plugin.Android.AugustLock.AugustLockDevice(lockAddr, key, keyOffset);
            _ = identifyAugustLock();

            //generate or retrieve address
            string address = setupPrivateKey().Address;
            TextView addrText = FindViewById<TextView>(Resource.Id.address);
            addrText.Visibility = ViewStates.Visible;
            addrText.Text = address;
            Console.WriteLine("ADDRESS: " + address);
            addrText.Click += copyToClipboard;

            //Start connection watchdog timer
            Timer timer = new Timer();
            RestartServer callback = serve;
            timer.Schedule(new ConnectionWatchdog(timer, callback), 0, 60*1000); //run once per minute, check connection is still valid
        }

        private void copyToClipboard(object o, EventArgs e)
        {
            Clipboard.SetTextAsync(setupPrivateKey().Address);
            Snackbar.Make(layout, "Copied device Address to Clipboard",
                    Snackbar.LengthShort).Show();
        }

        private Account setupPrivateKey()
        {
            var privateKeyString = Preferences.Get("private_key", "");
            if (privateKeyString.Length == 0)
            {
                var privateKeyBytes = new byte[32];
                RandomNumberGenerator.Create().GetBytes(privateKeyBytes);
                privateKeyString = "0x" + BitConverter.ToString(privateKeyBytes).Replace("-", string.Empty);
                Preferences.Set("private_key", privateKeyString);
            }

            return new Account(privateKeyString);
        }

        private EthECKey setupEthKey()
        {
            var privateKeyString = Preferences.Get("private_key", "");
            if (privateKeyString.Length == 0)
            {
                var privateKeyBytes = new byte[32];
                RandomNumberGenerator.Create().GetBytes(privateKeyBytes);
                privateKeyString = "0x" + BitConverter.ToString(privateKeyBytes).Replace("-", string.Empty);
                Preferences.Set("private_key", privateKeyString);
            }

            return new EthECKey(privateKeyString);
        }

        private async Task identifyAugustLock()
        {
            august.DebugMessage += (src, msg) => Android.Util.Log.Debug("August", msg);
            IDevice lockDev = await august.findLock();
            if (lockDev != null)
            {
                TextView found = FindViewById<TextView>(Resource.Id.found);
                TextView scanning = FindViewById<TextView>(Resource.Id.scanning);
                found.Visibility = ViewStates.Visible;
                scanning.Visibility = ViewStates.Gone;
                fab.Visibility = ViewStates.Visible;
            }
        }

        private void serve()
        {
            ReceivedMessage callback = ReceivedClientMessage;
            //AsynchronousClient asc = new AsynchronousClient();
            SynchronousSocketClient.StartClient(setupPrivateKey().Address, callback);
        }

        private void ReceivedClientMessage(byte[] result)
        {
            Console.WriteLine("Processing: " + BitConverter.ToString(result).Replace("-", string.Empty));

            byte opcode = result[0];
            byte[] msg = new byte[result.Length - 1];
            Array.Copy(result, 1, msg, 0, result.Length - 1);

            switch (opcode)
            {
                case 0x01:
                    break;
                case 0x02:
                    //challenge, we sign it
                    Console.WriteLine($"Please Sign: {msg}");
                    byte[] sig = signChallenge(msg);
                    Console.WriteLine("MSG: " + BitConverter.ToString(msg).Replace("-", string.Empty));
                    Console.WriteLine("SIG: " + BitConverter.ToString(sig).Replace("-", string.Empty));
                    //and send back
                    SynchronousSocketClient.SendChallengeResponse(sig);
                    break;
                case 0x04:
                    //text request
                    Console.WriteLine("MSG: " + System.Text.Encoding.UTF8.GetString(msg));
                    handleCommand(msg);
                    break;
                case 0x06:
                    //keepalive
                    SynchronousSocketClient.SendKeepAlive();
                    break;
            }
        }

        private string getArg(byte[] rq, int index)
        {
            int argLen = rq[index];
          
            byte[] thisArg = new byte[argLen];
            Array.Copy(rq, index + 1, thisArg, 0, argLen);
            return System.Text.Encoding.UTF8.GetString(thisArg);
        }

        private void handleCommand(byte[] rq)
        {
            //isolate into commands
            var args = new Dictionary<string, string>();
            string command = getArg(rq, 0);
            int index = command.Length + 1;
    
            while (index < rq.Length)
            {
                string arg = getArg(rq, index);
                index += arg.Length + 1;
                if (index >= rq.Length) break;
                string param = getArg(rq, index);
                index += param.Length + 1;
                args[arg] = param;
            }

            if (command.StartsWith("getChallenge"))
            {
                currentChallenge = getNewChallenge();
                //send challenge back
                SynchronousSocketClient.SendResponse(currentChallenge);
            }
            else if (command.StartsWith("checkSignature"))
            {
                if (currentChallenge.Length == 0) return; //protect against replay attacks with blank challenge

                string signatureHex = args["sig"];
                Console.WriteLine("SIG: " + signatureHex);

                //check the address of this guy
                var signer = new EthereumMessageSigner();
                var addressRecovered = signer.EncodeUTF8AndEcRecover(currentChallenge, signatureHex);
                currentChallenge = ""; //Protect against replay attacks
                Console.WriteLine("Recovered address: " + addressRecovered);
                _ = checkTokenBalanceAndUnlock(addressRecovered);
            }
        }

        [Function("balanceOf", "uint256")]
        public class BalanceOfFunction : FunctionMessage
        {
            [Parameter("address", "_owner", 1)] public string Owner { get; set; }
        }

        private async Task<bool> checkTokenBalanceAndUnlock(string address)
        //private async Task checkTokenBalanceAndUnlock()
        {
            var tokenContractAddress = "0x6c658C19d68b8738470EDc2D0692cBFc94808EdD";
            var web3 = new Web3("https://rinkeby.infura.io/v3/48a611a8ab0d4b6d91ffd2f0eeae389e");

            var balanceOfMessage = new BalanceOfFunction() { Owner = address };

            SynchronousSocketClient.ShutDown();

            //Creating a new query handler
            var queryHandler = web3.Eth.GetContractQueryHandler<BalanceOfFunction>();

            var balance = await queryHandler
                .QueryAsync<BigInteger>(tokenContractAddress, balanceOfMessage)
                .ConfigureAwait(false);

            if (balance.CompareTo(BigInteger.Zero) > 0)
            {
                LockOrUnlock();
                //open!
            }

            Console.WriteLine("CONTINUE");

            serve();

            return true;
        }

        private string getNewChallenge()
        {
            System.Random rnd = new System.Random();
            int index = rnd.Next(challengeWords.Length);
            string challengePhrase = challengeWords[index] + "-";
            byte[] challengeNumber = new byte[10];
            RandomNumberGenerator.Create().GetBytes(challengeNumber);
            challengePhrase += BitConverter.ToString(challengeNumber).Replace("-", string.Empty);
            return challengePhrase;
        }

        //TODO: Move to UI thread
        private string setChallengeText(string newChallenge)
        {
            currentChallenge = newChallenge;
            TextView chText = FindViewById<TextView>(Resource.Id.challenge_text);
            chText.Visibility = ViewStates.Visible;
            chText.Text = newChallenge;
            return newChallenge;
        }

        private byte[] signChallenge(byte[] msg)
        {
            var signer = new EthereumMessageSigner();
            var signature = signer.Sign(msg, setupEthKey());
            var addressRec1 = signer.EcRecover(msg, signature);
            Console.WriteLine("REC ADDR2: " + addressRec1);

            return StringToByteArray(signature);
        }

        public static byte[] StringToByteArray(string hex)
        {
            if (hex.StartsWith("0x"))
            {
                hex = hex.Substring(2);
            }

            return Enumerable.Range(0, hex.Length / 2).Select(x => Convert.ToByte(hex.Substring(x * 2, 2), 16)).ToArray();
        }

        FloatingActionButton fab;

        static readonly int REQUEST_LOCATION = 0;
        View layout;

        public void GetLocationPermission()
        {
            Log.Info("RQ", "Show Location button pressed. Checking permission.");

            // Check if the Camera permission is already available.
            if (ActivityCompat.CheckSelfPermission(this, Manifest.Permission.AccessFineLocation) != (int)Permission.Granted)
            {
                // Camera permission has not been granted
                RequestLocationPermission();
            }
        }

        void RequestLocationPermission()
        {
            Log.Info("RQ", "CAMERA permission has NOT been granted. Requesting permission.");

            if (ActivityCompat.ShouldShowRequestPermissionRationale(this, Manifest.Permission.AccessFineLocation))
            {
                // Provide an additional rationale to the user if the permission was not granted
                // and the user would benefit from additional context for the use of the permission.
                // For example if the user has previously denied the permission.
                Log.Info("RQ", "Displaying location permission rationale to provide additional context.");

                Snackbar.Make(layout, "Needs Location",
                    Snackbar.LengthIndefinite).SetAction("Ok", new Action<View>(delegate (View obj)
                    {
                        ActivityCompat.RequestPermissions(this, new String[] { Manifest.Permission.AccessFineLocation }, REQUEST_LOCATION);
                    })).Show();
            }
            else
            {
                // Camera permission has not been granted yet. Request it directly.
                ActivityCompat.RequestPermissions(this, new String[] { Manifest.Permission.AccessFineLocation }, REQUEST_LOCATION);
            }
        }

        public override bool OnCreateOptionsMenu(IMenu menu)
        {
            MenuInflater.Inflate(Resource.Menu.menu_main, menu);
            return true;
        }

        public override bool OnOptionsItemSelected(IMenuItem item)
        {
            int id = item.ItemId;
            if (id == Resource.Id.action_settings)
            {
                return true;
            }

            return base.OnOptionsItemSelected(item);
        }

        private void FabOnClick(object sender, EventArgs eventArgs)
        {
            _ = LockOrUnlock();
        }

        private async Task<bool> LockOrUnlock()
        {
            august.DebugMessage += (src, msg) => Android.Util.Log.Debug("August", msg);
            if (await august.Connect())
            {
                try
                {
                    bool isLocked = await august.Toggle();
                    handledLockState(isLocked);
                }
                catch (Exception e)
                {
                    //
                }
                finally
                {
                    august.Disconnect();
                }
            }
            else
            {
                // out of range, timeout or other error...
            }

            return true;
        }

        private void handledLockState(bool isLocked)
        {
            ImageView iv = FindViewById<ImageView>(Resource.Id.lock_icon);
            if (isLocked)
            {
                iv.SetImageDrawable(GetDrawable(Resource.Drawable.ic_locked));
            }
            else
            {
                iv.SetImageDrawable(GetDrawable(Resource.Drawable.ic_unlocked));
            }
        }

        public override void OnRequestPermissionsResult(int requestCode, string[] permissions, Permission[] grantResults)
        {
            if (requestCode == REQUEST_LOCATION)
            {
                // Received permission result for camera permission.
                Log.Info("RQ", "Received response for Camera permission request.");

                // Check if the only required permission has been granted
                if (grantResults.Length == 1 && grantResults[0] == Permission.Granted)
                {
                    // Camera permission has been granted, preview can be displayed
                    Log.Info("RQ", "Location permission has now been granted. Showing preview.");
                    Snackbar.Make(fab, "yay", Snackbar.LengthShort).Show();
                }
                else
                {
                    Log.Info("RQ", "CAMERA permission was NOT granted.");
                    Snackbar.Make(layout, "oh no", Snackbar.LengthShort).Show();
                }
            }
            else
            {
                base.OnRequestPermissionsResult(requestCode, permissions, grantResults);
            }
        }
    }

    public class SynchronousSocketClient
    {
        static Socket sender = null;
        static string keyAddress = null;

        public static async Task<Boolean> StartClient(string address, ReceivedMessage rcCallback)
        {
            // Data buffer for incoming data.  
            byte[] bytes = new byte[1024];

            return await Task.Run<Boolean>(() =>
            {
                // Connect to a remote device.  
                
                try
                {
                    // Establish the remote endpoint for the socket.  
                    //IPHostEntry ipHostInfo = Dns.GetHostEntry("192.168.50.9");
                    //IPAddress ipAddress = ipHostInfo.AddressList[0];
                    //IPEndPoint remoteEP = new IPEndPoint(ipAddress, port);

                    //IPHostEntry ipHostInfo = Dns.GetHostEntry("http://117.78.44.115/");
                    //IPAddress ipAddress = new IPAddress("http://117.78.44.115/");// ipHostInfo.AddressList[0];
                    System.Net.IPAddress ipAddress = System.Net.IPAddress.Parse("117.78.44.115");
                    IPEndPoint remoteEP = new IPEndPoint(ipAddress, 8081);

                    // Create a TCP/IP  socket.  
                    sender = new Socket(ipAddress.AddressFamily,
                        SocketType.Stream, ProtocolType.Tcp);

                    // Connect the socket to the remote endpoint. Catch any errors.  
                    try
                    {
                        sender.Connect(remoteEP);

                        Console.WriteLine("Socket connected to {0}",
                            sender.RemoteEndPoint.ToString());

                        keyAddress = address;

                        // Encode the data string into a byte array.  
                        byte[] addrBytes = MainActivity.StringToByteArray(address); //Encoding.ASCII.GetBytes(address);

                        byte[] msg = new byte[addrBytes.Length + 1];
                        msg[0] = 0x01;
                        Array.Copy(addrBytes, 0, msg, 1, addrBytes.Length);

                        // Send the data through the socket.  
                        int bytesSent = sender.Send(msg);

                        while (true)
                        {
                            // Receive the response from the remote device.  
                            int bytesRec = sender.Receive(bytes);
                            byte[] rcv = new byte[bytesRec];
                            Array.Copy(bytes, 0, rcv, 0, bytesRec);
                           
                            Console.WriteLine("Echoed test = {0}",
                                BitConverter.ToString(rcv).Replace("-", string.Empty));
                            rcCallback(rcv);
                        }

                        Console.WriteLine("END");

                        // Release the socket.  
                        //sender.Shutdown(SocketShutdown.Both);
                        //sender.Close();
                    }
                    catch (ArgumentNullException ane)
                    {
                        Console.WriteLine("ArgumentNullException : {0}", ane.ToString());
                    }
                    catch (SocketException se)
                    {
                        Console.WriteLine("SocketException : {0}", se.ToString());
                    }
                    catch (Exception e)
                    {
                        Console.WriteLine("Unexpected exception : {0}", e.ToString());
                    }

                }
                catch (Exception e)
                {
                    Console.WriteLine(e.ToString());
                }
                finally
                {
                    keyAddress = null;
                    if (sender != null && sender.Connected)
                    {
                        sender.Shutdown(SocketShutdown.Both);
                        sender.Close();
                    }
                }

                return false;
            });
        }

        internal static bool isActive()
        {
            return keyAddress != null;
        }

        internal static void ShutDown()
        {
            if (sender != null && sender.Connected)
            {
                sender.Shutdown(SocketShutdown.Both);
                sender.Close();
            }
        }

        internal static void SendChallengeResponse(byte[] sig)
        {
            // Encode the data string into a byte array.  
            byte[] addrBytes = MainActivity.StringToByteArray(keyAddress); //Encoding.ASCII.GetBytes(address);

            byte[] msg = new byte[addrBytes.Length + sig.Length + 1];
            msg[0] = 0x03;
            Array.Copy(addrBytes, 0, msg, 1, addrBytes.Length);
            Array.Copy(sig, 0, msg, 1 + addrBytes.Length, sig.Length);

            // Send the data through the socket.  
            sender.Send(msg);
        }

        internal static void SendResponse(string challenge)
        {
            // Encode the data string into a byte array.  
            //byte[] addrBytes = MainActivity.StringToByteArray(keyAddress); //Encoding.ASCII.GetBytes(address);

            byte[] msg = new byte[challenge.Length+1];
            msg[0] = 0x05;

            Array.Copy(Encoding.ASCII.GetBytes(challenge), 0, msg, 1, challenge.Length);

            // Send the data through the socket.  
            sender.Send(msg);
        }

        internal static void SendKeepAlive()
        {
            byte[] msg = new byte[1];
            msg[0] = 0x06;
            sender.Send(msg);
        }
    }

    class ConnectionWatchdog : TimerTask
    {
        Timer mTimer;
        RestartServer restartCallback;
        public ConnectionWatchdog(Timer timer, RestartServer callback)
        {
            this.mTimer = timer;
            this.restartCallback = callback;
        }
    
        public override void Run()
        {
            if (!SynchronousSocketClient.isActive())
            {
                restartCallback();
            }
        }
    }
}