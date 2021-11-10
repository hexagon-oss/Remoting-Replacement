# Remoting-Replacement
A library that replaces the missing .NET Remoting feature in .NET 5.0 and later

The new .NET Core runtime that is used in all .NET versions starting from 5.0 lacks some features of the .NET Framework 4.8 runtime. 
One of these missing features is "NET Remoting", an easy-to-use RPC (remote procedure call) framework. Because we have a project 
that heavily relies on this feature, I decided to reimplement its features so our project can be upgraded. 

# Advantages
- **Remoting-Replacement** implements a fully transparent RPC framework, that can be used as a plug-in-replacement for applications
that relied on .NET Remoting. Only minor changes to the existing code are required, namely in establishing the connection between
client and server. 
- No complex attributing in code required to define the RPC api. Marking classes as `[Serializable]` or deriving from `MarshalByRefObject` 
is sufficient to declare call-by-value or call-by-reference semantics. 
- Calls are fully transparent for both the server and the client. Neither end has to know that the operation happens on a remote instance.
- Full support for bidirectional communication. Event callbacks are supported as well as callbacks using interfaces.
- Support for multiple clients and multiple servers within the same application.
- Automatic distribution of processes across several computers in a network

# Applications
- Distributed computing
- Machine control
- plus many more

# Limitations
- It is recommended that client and server execute the same code base, because internally, binary serialization is used for performance reasons.
The code can be distributed automatically.
- The classes involved in call-by-reference semantics must observe some limiations. Call-by-reference works only on virtual methods or interfaces. 
(Background: Internally, https://github.com/castleproject/Core is used to create the remoting proxies. Proxies can only be added to virtual methods or as interfaces)
- TBD: There's no security mechanism implemented yet. A remoting server is - by design - vulnerable to remote code execution attacks.
