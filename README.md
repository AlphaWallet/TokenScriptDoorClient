# TokenScriptDoorClient
TokenScriptDoorClient


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
