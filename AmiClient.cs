// Copyright © 2017 Alex Forster. All rights reserved.

namespace Ami
{
    using System;
    using System.Diagnostics;
    using System.Text;
    using System.Threading;
    using System.Threading.Tasks;
    using System.Collections.Generic;
    using System.Collections.Concurrent;
    using System.Linq;
    using System.IO;
    using System.Security.Cryptography;

    public sealed partial class AmiClient
    {
        private readonly Stream stream;

        private readonly Thread worker;

        public AmiClient(Stream stream)
        {
            this.stream = stream ?? throw new ArgumentNullException(nameof(stream));

            Debug.Assert(stream.CanRead);
            Debug.Assert(stream.CanWrite);

            this.worker = new Thread(this.WorkerMain) { IsBackground = true };

            this.worker.Start();
        }

        public async Task<Boolean> Login(String username, String secret, Boolean md5 = true)
        {
            if(username == null)
            {
                throw new ArgumentNullException(nameof(username));
            }

            if(secret == null)
            {
                throw new ArgumentNullException(nameof(secret));
            }

            AmiMessage request, response;

            if(md5)
            {
                request = new AmiMessage
                {
                    { "Action", "Challenge" },
                    { "AuthType", "MD5" },
                };

                response = await this.Publish(request);

                if(!(response["Response"] ?? String.Empty).Equals("Success", StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }

                var challengeResponse = MD5.Create()
                                           .ComputeHash(Encoding.ASCII.GetBytes(response["Challenge"] + secret));

                var key = "";

                for(var i = 0; i < challengeResponse.Length; i++)
                {
                    key += challengeResponse[i].ToString("x2");
                }

                request = new AmiMessage
                {
                    { "Action", "Login" },
                    { "AuthType", "MD5" },
                    { "Username", username },
                    { "Key", key },
                };

                response = await this.Publish(request);
            }
            else
            {
                request = new AmiMessage
                {
                    { "Action", "Login" },
                    { "Username", username },
                    { "Secret", secret },
                };

                response = await this.Publish(request);
            }

            return (response["Response"] ?? String.Empty).Equals("Success", StringComparison.OrdinalIgnoreCase);
        }

        public async Task<Boolean> Logoff()
        {
            AmiMessage request, response;

            request = new AmiMessage
            {
                { "Action", "Logoff" },
            };

            response = await this.Publish(request);

            return (response["Response"] ?? String.Empty).Equals("Goodbye", StringComparison.OrdinalIgnoreCase);
        }
    }
}
