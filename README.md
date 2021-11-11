# TokenScriptDoorClient

This is an alpha app which provides an ECDSA secure interface between the Bluetooth link to an August Door lock and the TokenScript ScriptProxy bridge (A Springboot running on a remote server).

The comms between a typical TokenScript and the device look like this:

1. DoorClient (this app) sends a login ping to the the ScriptProxy.
2. ScriptProxy responds with a 16 bytes SecureRandom challenge.
3. DoorClient signs the challenge with its private key and returns the signature.
4. Once authenticated, the ScriptProxy EC-recovers the DoorClient's Ethereum address and maps the client to that address.
5. The TokenScript running on AlphaWallet uses the DoorClient's Ethereum address as a comms address.
6. The TokenScript pings the ScriptProxy with a request for human-readable challenge from a device with the DoorClient's Eth address.
7. The ScriptProxy forwards the request to the client mapped to the Eth address.
8. The client (this app) receives the challenge request and replies with a new challenge eg 'BreadFruit-34FA12BACDE233'
9. ScriptProxy forwards the challenge back to the TokenScript running on AW.
10. The TokenScript displays the challenge to the user, who may now sign using SignPersonalMessage.
11. The signature is returned via the ScriptProxy to the DoorClient app.
12. The DoorClient app EC-recovers the signing address from the signature using the current challenge (the BreadFruit value from 8).
13. The DoorClient app checks the designated entry Token balance of this signing address.
14. If the signing address holds the designated entry token, the app unlocks the door via the Bluetooth connection.


This project uses an adapted version of the Library here:

https://github.com/Marcus-L/xamarin-august-ble

Addendum and notes to the method from the above repo for finding the BlueTooth keys (how I got it):

###NB - note that this method CAN'T be used in an attack against your door, notice that you provide permission in one of the steps.

- requires you to have a second SIM card (different to the one tied to your main phone which operates the lock with the approved app)
- First sign up with your normal phone as per the lock instructions
- Invite a new user with full access using your second SIM. 
- If you have a rooted Android phone you can install Yale Access on that and skip the following two instructions
- 	Follow instructions here to open up an emulator running as root: https://stackoverflow.com/questions/43923996/adb-root-is-not-working-on-emulator-cannot-run-as-root-in-production-builds/45668555#45668555
-   Install Google Play Store app from a download site and install Yale Access (Use google to work out how - I pulled the Yale Access APK from a separate phone).
- Send an invite from your Yale Access app on your main phone to your second phone number.
- Log into the Yale Access app on the rooted device/emulator and complete the linking as per instructions (you have to enter two codes, from phone and email).
- Once you see your house name in the app you can pull the details. Try to open the lock.
- In a file browser (Use either the device browser or Android Studio, or even Windows Explorer etc) go to 
	/data/data/com.august.bennu/shared_prefs
- Open the file called 'PeripheralInfoCache.xml', and pull the following three params:
'bluetoothAddress'
'handshakeKey'
'handshakeKeyIndex'
If you can't see all of these params, then you haven't yet completed setup. You need to have successfully opened the lock.
- Open the ```MainActivity.cs``` file in the project
- Locate the three params at the top:
```
		private string lockAddr = "<YOUR LOCK BLUETOOTH ADDR>"; //eg 125623562356 - 'bluetoothAddress' from keyfile, with colons removed
    private string key = "<handshake key>";  // 'handshakeKey' from keyfile, should be 32 hex characters    
    private int keyOffset = 1; // 'handshakeKeyIndex' from keyfile, usually is 1
```		
- Copy the values from above, not forgetting to remove the colons from the bluetooth address.

- Run the app on another phone (not emulator) and check that the bluetooth opening button now works on your lock!
