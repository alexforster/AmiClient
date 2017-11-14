## AmiClient

Asterisk Management Interface (AMI) client library for .NET

**Dependencies**

 * NetStandard.Library
 * System.Reactive.Interfaces
    * NetStandard.Library
/Users/alexforster/Desktop/AmiClient/AmiClient/README.md
### Quick Start

Here's an easy way to set up an Asterisk 13 development environment:

 1. Download a local copy of [this basic Asterisk configuration](https://github.com/asterisk/asterisk/tree/13/configs/basic-pbx) and place it in `~/Desktop/etc-asterisk` (or your preferred location)
 2. Run a local Asterisk 13 Docker container...

  ```bash
docker run -dit --rm --privileged \
           --name asterisk13-dev \
           -p 5060:5060 -p 5060:5060/udp \
           -p 10000-11000:10000-11000/udp \
           -p 5038:5038 \
           -v ~/Desktop/etc-asterisk:/etc/asterisk \
           cleardevice/docker-cert-asterisk13-ubuntu
```

 3. Use the below example as a starting point for learning the *AmiClient* API...

### Example Code

```csharp
using System;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using System.Net.Sockets;
using System.Reactive.Linq;

using Ami;

internal static class Program
{
    public static async Task Main(String[] args)
    {
        // To make testing possible, an AmiClient accepts any Stream object that is readable and writable.
        // This means that the user must establish a TCP connection to the Asterisk AMI server separately.

        using(var socket = new TcpClient(hostname: "localhost", port: 5038))
        using(var client = new AmiClient(socket.GetStream()))
        {
            // Activity on the wire can be logged/debugged with the DataSent and DataReceived events

            //client.DataSent += (s, e) => Console.Error.Write(e.Data);
            //client.DataReceived += (s, e) => Console.Error.Write(e.Data);

            // Log in

            if(!await client.Login(username: "admin", secret: "amp111", md5: true))
            {
                Console.WriteLine("Login failed");
                return;
            }

            // Issue a PJSIPShowEndpoints command.
            // Note: if not provided, a random ActionID is automatically generated.

            var showEndpointsResponse = await client.Publish(new AmiMessage
            {
                { "Action", "PJSIPShowEndpoints" },
            });

            Debug.Assert(showEndpointsResponse["Response"] == "Success");

            // After the PJSIPShowEndpoints command successfully executes, Asterisk will begin emitting
            // EndpointList events. Each EndpointList event represents a single PJSIP endpoint, and uses the
            // same ActionID as the PJSIPShowEndpoints command that caused it.

            // Once events have been emitted for all PJSIP endpoints, an EndpointListComplete event will
            // be emitted, again using the same ActionID as the PJSIPShowEndpoints command that caused it.

            Console.Out.WriteLine("=== Extensions ===");

            await client
                  .Where(message => message["ActionID"] == showEndpointsResponse["ActionID"])
                  .TakeWhile(message => message["Event"] != "EndpointListComplete")
                  .Do(message =>
                  {
                      Console.Out.WriteLine($"PJSIP/{message["ObjectName"]} ({message["DeviceState"]})");
                  });

            // Log off

            if(!await client.Logoff())
            {
                Console.WriteLine("Logoff failed");
                return;
            }
        }
    }
}
```
