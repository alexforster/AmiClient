/* Copyright © Alex Forster. All rights reserved.
 * https://github.com/alexforster/AmiClient/
 *
 * Licensed under the Apache License, Version 2.0 (the "License");
 * you may not use this file except in compliance with the License.
 * You may obtain a copy of the License at
 *
 *     http://www.apache.org/licenses/LICENSE-2.0
 *
 * Unless required by applicable law or agreed to in writing, software
 * distributed under the License is distributed on an "AS IS" BASIS,
 * WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
 * See the License for the specific language governing permissions and
 * limitations under the License.
 */

namespace Ami
{
    using System;
    using System.IO;
    using System.Text;
    using System.Collections;
    using System.Collections.Generic;
    using System.Linq;

    using ByteArrayExtensions;

    public sealed partial class AmiMessage
    {
        internal static readonly Byte[] TerminatorBytes = { 0x0d, 0x0a };

        internal static readonly Char[] TerminatorChars = { '\x0d', '\x0a' };

        public static AmiMessage FromBytes(Byte[] bytes)
        {
            var result = new AmiMessage();

            for(var nrLine = 1;; nrLine++)
            {
                var crlfPos = bytes.Find(AmiMessage.TerminatorBytes, 0, bytes.Length);

                if(crlfPos == -1)
                {
                    throw new ArgumentException($"unexpected end of message after {nrLine} line(s)", nameof(bytes));
                }

                var line = Encoding.UTF8.GetString(bytes.Slice(0, crlfPos));
                bytes = bytes.Slice(crlfPos + AmiMessage.TerminatorBytes.Length);

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
                    writer.Write(field.Key);
                    writer.Write(": ");
                    writer.Write(field.Value);
                    writer.Write(AmiMessage.TerminatorChars);
                }

                writer.Write(AmiMessage.TerminatorChars);
            }

            return stream.ToArray();
        }

        public override String ToString()
        {
            return Encoding.UTF8.GetString(this.ToBytes());
        }
    }
}
