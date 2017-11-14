// Copyright © 2017 Alex Forster. All rights reserved.

namespace Ami
{
    using System;
    using System.IO;
    using System.Text;
    using System.Collections;
    using System.Collections.Generic;
    using System.Linq;

    public sealed partial class AmiMessage
    {
        public static AmiMessage FromBytes(Byte[] bytes)
        {
            var result = new AmiMessage();

            var stream = new MemoryStream(bytes);

            var reader = new StreamReader(stream, new UTF8Encoding(false));

            for(var nrLine = 1; ; nrLine++)
            {
                var line = reader.ReadLine();

                if(line == null)
                {
                    throw new ArgumentException("unterminated message", nameof(bytes));
                }

                if(line.Equals(String.Empty))
                {
                    break; // empty line terminates
                }

                var kvp = line.Split(new[] { ':' }, 2);

                if(kvp.Length != 2)
                {
                    throw new ArgumentException($"malformed field on line {nrLine}", nameof(bytes));
                }

                result.Add(kvp[0], kvp[1]);
            }

            return result;
        }

        public static AmiMessage FromString(String @string)
        {
            var bytes = Encoding.UTF8.GetBytes(@string);

            return AmiMessage.FromBytes(bytes);
        }

        public Byte[] ToBytes()
        {
            var stream = new MemoryStream();

            using(var writer = new StreamWriter(stream, new UTF8Encoding(false)))
            {
                foreach(var field in this.Fields)
                {
                    writer.Write($"{field.Key}: {field.Value}\r\n");
                }

                writer.Write("\r\n");
            }

            return stream.ToArray();
        }

        public override String ToString()
        {
            return Encoding.UTF8.GetString(this.ToBytes());
        }
    }
}
