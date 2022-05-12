# Remoting-Replacement
A library that replaces the missing .NET Remoting feature in .NET 5.0 and later

The new .NET Core runtime that is used in all .NET versions starting from 5.0 lacks some features of the .NET Framework 4.8 runtime. 
One of these missing features is "NET Remoting", an easy-to-use RPC (remote procedure call) framework. Because we have a project 
that heavily relies on this feature, I decided to reimplement its functionality so our project can be ported to .NET Core. All the
available RPC frameworks for .NET (e.g. gRPC) require code annotation or explicit declaration of the remote APIs. Since we have
hundreds of interfaces that are used across network calls, this was not really an option.

# Advantages of Remoting
- **Remoting-Replacement** implements a fully transparent RPC framework, that can be used as a plug-in-replacement for applications
that relied on .NET Remoting. Only minor changes to the existing code are required, namely in establishing the connection between
client and server. 
- No complex attributing in code required to define the RPC api. Marking classes as `[Serializable]` or deriving from `MarshalByRefObject` 
is sufficient to declare call-by-value or call-by-reference semantics. 
- Calls are fully transparent for both the server and the client. Neither end has to know that the operation happens on a remote instance.
- Full support for bidirectional communication. Event callbacks are supported as well as callbacks using interfaces. There is no limit
to the number of simultanously active calls in either direction.
- Support for multiple clients and multiple servers within the same application.
- Transparent reference support. Clients can take references to objects that lie on any system within the distributed application.
- Automatic self-distribution of processes across several computers in a network
- Fast. For most applications, the remoting overhead is negligible.
- Support for encrypted connections using an SSL socket.
- Implemented in 100% C#, no native dependencies.

# Applications
- Distributed computing
- Machine control
- plus many more

# Limitations
- It is recommended that client and server execute the same code base, because internally, binary serialization is used for performance reasons.
The library and the application using it can distribute itself to arbitrary computers in the network.
- The classes involved in call-by-reference semantics are bound to some limiations. Call-by-reference works only on virtual methods or through interfaces. 
(Background: Internally, https://github.com/castleproject/Core is used to create the remoting proxies. Proxies can only be added to virtual methods or as interfaces)
- While the communication link may be encrypted, full trust between server and client is required. Either end can - by design - execute
arbitrary code on the other side.

# Usage

## Scenario 1: Offload calculations to another computer

In your application, reference the nuget packages `LeicaGeosystemsAG.NewRemoting` and `LeicaGeosystemsAG.NewRemoting.RemotingServer`. The first 
is the library, the second contains the stub loader to bootstrap the remote server. 

Then it's as easy as establishing a connection:

```csharp
var credentials = new Credentials("MyUserName", "MyPassword"); // Windows password of remote computer (needs admin rights)
m_remoteLoader = m_loaderFactory.Create(credentials, "RemoteComputerName", 4600);
m_remoteLoader.Connect(CancellationToken.None, null);

var service = m_remoteLoader.CreateObject<MyServerObject, IServerObject>();
// Start using the service. Any operation performed on the object will actually take place on the remote side.
```

For the above to work, the definition of `MyServerObject` must be in scope at the location of the call. It could be defined like this:

```csharp
public class MyServerObject : MarshalByRefObject, IServerObject
{
	// Any number of methods/properties
}
```
