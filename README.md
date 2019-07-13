# AmiClient

Asterisk Management Interface (AMI) client library for .NET

 * https://github.com/alexforster/AmiClient/
 * https://www.nuget.org/packages/AmiClient

**Features**

 * Designed to be used with `async/await`
 * Methods can be safely called from multiple threads
 * The `AmiClient` class is `IObservable` for use with Reactive Extensions (Rx)

**Dependencies**

 * `System.Reactive.Interfaces`

**Note:** While it's not a dependency, you will probably want to use the `System.Reactive.Linq` package alongside this library.

## Quick start

Here's an easy way to set up an Asterisk 13 development environment:

 1. Download a local copy of [this basic Asterisk configuration](https://github.com/asterisk/asterisk/tree/13/configs/basic-pbx) and place it in the directory `~/Desktop/etc-asterisk` (or your preferred location)
 2. Add a `manager.conf` file to your basic Asterisk configuration directory to enable the Asterisk Management Interface...

```
cat << EOF > ~/Desktop/etc-asterisk/manager.conf
; manager.conf

[general]
enabled = yes
bindaddr = 0.0.0.0
port = 5038
timestampevents = yes ; (optional but helpful)

[admin]
secret = amp111
read = all
write = all

EOF
```

 3. Build an Asterisk 13 Docker image...

```bash
cat <<'EOF' | docker build -t asterisk13 -
FROM amd64/ubuntu:cosmic
SHELL ["/bin/bash", "-eu", "-c"]

RUN \
	export DEBIAN_FRONTEND=noninteractive; \
	apt-get update; \
	apt-get -y install ca-certificates apt-utils; \
	apt-get -y install asterisk;

VOLUME /etc/asterisk
USER asterisk:asterisk
EXPOSE 5038/tcp
EXPOSE 5060/udp
EXPOSE 10000-10500/udp

ENTRYPOINT ["/usr/sbin/asterisk", "-vvvvvvvvv", "-ddddddddd", "-c"]
EOF
```

 4. Run your new Asterisk 13 Docker image in a temporary container...

```bash
docker run -dit --rm --privileged \
         --name asterisk13-dev \
         -p 5060:5060 -p 5060:5060/udp \
         -p 10000-10500:10000-10500/udp \
         -p 5038:5038 \
         -v ~/Desktop/etc-asterisk:/etc/asterisk \
         asterisk13
```

 5. Use the example code below as a starting point for learning the *AmiClient* API...

### Example code

```csharp
using System;
using System.Net.Sockets;
using System.Threading.Tasks;
using System.Reactive.Linq;

using Ami;

namespace Playground
{
   internal static class Program
   {
     public static async Task Main(String[] args)
     {
       // To make testing possible, an AmiClient accepts any Stream object
       // that is readable and writable. This means that the user is
       // responsible for maintaining a TCP connection to the AMI server.

       // It's actually pretty easy...

       using(var socket = new TcpClient(hostname: "127.0.0.1", port: 5038))
       using(var client = new AmiClient(socket.GetStream()))
       {
         // At this point, we've completed the AMI protocol handshake and
         // a background I/O Task is consuming data from the socket.

         // Activity on the wire can be observed and logged using the
         // DataSent and DataReceived events...

         client.DataSent += (s, e) => Console.Error.Write(e.Data);
         client.DataReceived += (s, e) => Console.Error.Write(e.Data);

         // First, let's authenticate using the Login() helper function...

         if(!await client.Login(username: "admin", secret: "amp111", md5: true))
         {
            Console.WriteLine("Login failed");
            return;
         }

         // In case the Asterisk server hasn't finished booting, let's wait
         // until it's ready...

         await client.Where(message => message["Event"] == "FullyBooted").FirstAsync();

         // Now let's issue a PJSIPShowEndpoints command...

         var response = await client.Publish(new AmiMessage
         {
            { "Action", "PJSIPShowEndpoints" },
         });

         // Because we didn't specify an ActionID, one was implicitly
         // created for us by the AmiMessage object. That's how we track
         // requests and responses, allowing this client to be used
         // by multiple threads or tasks.

         if(response["Response"] == "Success")
         {
            // After the PJSIPShowEndpoints command successfully executes,
            // Asterisk will begin emitting EndpointList events.

            // Each EndpointList event represents a single PJSIP endpoint,
            // and has the same ActionID as the PJSIPShowEndpoints command
            // that caused it.

            // Once events have been emitted for all PJSIP endpoints,
            // an EndpointListComplete event will be emitted, again with
            // the same ActionID as the PJSIPShowEndpoints command
            // that caused it.

            // Using System.Reactive.Linq, all of that can be modeled with
            // a simple Rx IObservable consumer...

            await client
               .Where(message => message["ActionID"] == response["ActionID"])
               .TakeWhile(message => message["Event"] != "EndpointListComplete")
               .Do(message => Console.Out.WriteLine($"~~~ \"{message["ObjectName"]}\" ({message["DeviceState"]}) ~~~"));
         }

         // We're done, so let's be a good client and use the Logoff()
         // helper function...

         if(!await client.Logoff())
         {
            Console.WriteLine("Logoff failed");
            return;
         }
       }
     }
   }
}
```

### Example output

```
Action: Challenge
ActionID: 24871f64-ca7e-4dfb-a67f-734ba3893efb
AuthType: MD5

Response: Success
ActionID: 24871f64-ca7e-4dfb-a67f-734ba3893efb
Challenge: 158303519

Action: Login
ActionID: 214fdbe7-7c7c-4ead-abfe-037a3d4cedff
AuthType: MD5
Username: admin
Key: 6a717a8d45e8445832d5d088862654e9

Response: Success
ActionID: 214fdbe7-7c7c-4ead-abfe-037a3d4cedff
Message: Authentication accepted

Event: FullyBooted
Privilege: system,all
Status: Fully Booted

Action: PJSIPShowEndpoints
ActionID: 3ec566db-feec-4758-bc41-407ae70491c2

Event: SuccessfulAuth
Privilege: security,all
EventTV: 2019-01-21T19:56:27.148+0000
Severity: Informational
Service: AMI
EventVersion: 1
AccountID: admin
SessionID: 0x7fcb00000d50
LocalAddress: IPV4/TCP/0.0.0.0/5038
RemoteAddress: IPV4/TCP/172.17.0.1/44628
UsingPassword: 0
SessionTV: 2019-01-21T19:56:27.148+0000

Response: Success
ActionID: 3ec566db-feec-4758-bc41-407ae70491c2
EventList: start
Message: A listing of Endpoints follows, presented as EndpointList events

Event: EndpointList
ActionID: 3ec566db-feec-4758-bc41-407ae70491c2
ObjectType: endpoint
ObjectName: 1107
Transport: 
Aor: 1107
Auths: 1107
OutboundAuths: 
Contacts: 
DeviceState: Unavailable
ActiveChannels: 

~~~ "1107" (Unavailable) ~~~
Event: EndpointList
ActionID: 3ec566db-feec-4758-bc41-407ae70491c2
ObjectType: endpoint
ObjectName: 1113
Transport: 
Aor: 1113
Auths: 1113
OutboundAuths: 
Contacts: 
DeviceState: Unavailable
ActiveChannels: 

~~~ "1113" (Unavailable) ~~~
Event: EndpointList
ActionID: 3ec566db-feec-4758-bc41-407ae70491c2
ObjectType: endpoint
ObjectName: 1106
Transport: 
Aor: 1106
Auths: 1106
OutboundAuths: 
Contacts: 
DeviceState: Unavailable
ActiveChannels: 

~~~ "1106" (Unavailable) ~~~
Event: EndpointList
ActionID: 3ec566db-feec-4758-bc41-407ae70491c2
ObjectType: endpoint
ObjectName: 1101
Transport: 
Aor: 1101
Auths: 1101
OutboundAuths: 
Contacts: 
DeviceState: Unavailable
ActiveChannels: 

~~~ "1101" (Unavailable) ~~~
Event: EndpointList
ActionID: 3ec566db-feec-4758-bc41-407ae70491c2
ObjectType: endpoint
ObjectName: 1103
Transport: 
Aor: 1103
Auths: 1103
OutboundAuths: 
Contacts: 
DeviceState: Unavailable
ActiveChannels: 

~~~ "1103" (Unavailable) ~~~
Event: EndpointList
ActionID: 3ec566db-feec-4758-bc41-407ae70491c2
ObjectType: endpoint
ObjectName: 1102
Transport: 
Aor: 1102
Auths: 1102
OutboundAuths: 
Contacts: 
DeviceState: Unavailable
ActiveChannels: 

~~~ "1102" (Unavailable) ~~~
Event: EndpointList
ActionID: 3ec566db-feec-4758-bc41-407ae70491c2
ObjectType: endpoint
ObjectName: 1114
Transport: 
Aor: 1114
Auths: 1114
OutboundAuths: 
Contacts: 
DeviceState: Unavailable
ActiveChannels: 

~~~ "1114" (Unavailable) ~~~
Event: EndpointList
ActionID: 3ec566db-feec-4758-bc41-407ae70491c2
ObjectType: endpoint
ObjectName: 1115
Transport: 
Aor: 1115
Auths: 1115
OutboundAuths: 
Contacts: 
DeviceState: Unavailable
ActiveChannels: 

~~~ "1115" (Unavailable) ~~~
Event: EndpointList
ActionID: 3ec566db-feec-4758-bc41-407ae70491c2
ObjectType: endpoint
ObjectName: 1109
Transport: 
Aor: 1109
Auths: 1109
OutboundAuths: 
Contacts: 
DeviceState: Unavailable
ActiveChannels: 

~~~ "1109" (Unavailable) ~~~
Event: EndpointList
ActionID: 3ec566db-feec-4758-bc41-407ae70491c2
ObjectType: endpoint
ObjectName: 1110
Transport: 
Aor: 1110
Auths: 1110
OutboundAuths: 
Contacts: 
DeviceState: Unavailable
ActiveChannels: 

~~~ "1110" (Unavailable) ~~~
Event: EndpointList
ActionID: 3ec566db-feec-4758-bc41-407ae70491c2
ObjectType: endpoint
ObjectName: dcs-endpoint
Transport: 
Aor: dcs-aor
Auths: 
OutboundAuths: dcs-auth
Contacts: dcs-aor/sip:sip.digiumcloud.net,
DeviceState: Not in use
ActiveChannels: 

~~~ "dcs-endpoint" (Not in use) ~~~
Event: EndpointList
ActionID: 3ec566db-feec-4758-bc41-407ae70491c2
ObjectType: endpoint
ObjectName: 1111
Transport: 
Aor: 1111
Auths: 1111
OutboundAuths: 
Contacts: 
DeviceState: Unavailable
ActiveChannels: 

~~~ "1111" (Unavailable) ~~~
Event: EndpointList
ActionID: 3ec566db-feec-4758-bc41-407ae70491c2
ObjectType: endpoint
ObjectName: 1105
Transport: 
Aor: 1105
Auths: 1105
OutboundAuths: 
Contacts: 
DeviceState: Unavailable
ActiveChannels: 

~~~ "1105" (Unavailable) ~~~
Event: EndpointList
ActionID: 3ec566db-feec-4758-bc41-407ae70491c2
ObjectType: endpoint
ObjectName: 1108
Transport: 
Aor: 1108
Auths: 1108
OutboundAuths: 
Contacts: 
DeviceState: Unavailable
ActiveChannels: 

~~~ "1108" (Unavailable) ~~~
Event: EndpointList
ActionID: 3ec566db-feec-4758-bc41-407ae70491c2
ObjectType: endpoint
ObjectName: 1112
Transport: 
Aor: 1112
Auths: 1112
OutboundAuths: 
Contacts: 
DeviceState: Unavailable
ActiveChannels: 

~~~ "1112" (Unavailable) ~~~
Event: EndpointList
ActionID: 3ec566db-feec-4758-bc41-407ae70491c2
ObjectType: endpoint
ObjectName: 1104
Transport: 
Aor: 1104
Auths: 1104
OutboundAuths: 
Contacts: 
DeviceState: Unavailable
ActiveChannels: 

~~~ "1104" (Unavailable) ~~~
Event: EndpointListComplete
ActionID: 3ec566db-feec-4758-bc41-407ae70491c2
EventList: Complete
ListItems: 16

Action: Logoff
ActionID: e8427d14-04c4-43f3-9f03-e9dd2fc1b891

Response: Goodbye
ActionID: e8427d14-04c4-43f3-9f03-e9dd2fc1b891
Message: Thanks for all the fish.

```

## Public API

```csharp
public sealed class AmiMessage : IEnumerable<KeyValuePair<String, String>>
{
   // creation

   public AmiMessage();

   public DateTimeOffset Timestamp { get; }

   // deserialization

   public static AmiMessage FromBytes(Byte[] bytes);

   public static AmiMessage FromString(String @string);

   // direct field access

   public readonly List<KeyValuePair<String, String>> Fields;

   // field initialization support

   public void Add(String key, String value);

   // field indexer support

   public String this[String key] { get; set; }

   // field enumeration support

   public IEnumerator<KeyValuePair<String, String>> GetEnumerator();

   IEnumerator IEnumerable.GetEnumerator();

   // serialization

   public Byte[] ToBytes();

   public override String ToString();
}

public sealed class AmiClient : IDisposable, IObservable<AmiMessage>
{
   // creation

   public AmiClient();

   public AmiClient(Stream stream);

   public Stream Stream { get; set; } 

   // AMI protocol helpers

   public async Task<Boolean> Login(String username, String secret, Boolean md5 = true);

   public async Task<Boolean> Logoff();

   // AMI protocol debugging

   public sealed class DataEventArgs : EventArgs
   {
       public readonly Byte[] Data;
   }

   public event EventHandler<DataEventArgs> DataSent;

   public event EventHandler<DataEventArgs> DataReceived;

   // request/reply

   public async Task<AmiMessage> Publish(AmiMessage action);

   // IObservable<AmiMessage>

   public IDisposable Subscribe(IObserver<AmiMessage> observer);

   public void Unsubscribe(IObserver<AmiMessage> observer);

   public void Dispose();
}
```
