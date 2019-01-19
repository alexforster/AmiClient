/* Copyright © 2019 Alex Forster. All rights reserved.
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

	public sealed partial class AmiMessage
	{
		public static AmiMessage FromBytes(Byte[] bytes)
		{
			var result = new AmiMessage();

			var stream = new MemoryStream(bytes);

			var reader = new StreamReader(stream, new UTF8Encoding(false));

			for(var nrLine = 1;; nrLine++)
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
					writer.Write($"{field.Key}: {field.Value}\x0d\x0a");
				}

				writer.Write("\x0d\x0a");
			}

			return stream.ToArray();
		}

		public override String ToString()
		{
			return Encoding.UTF8.GetString(this.ToBytes());
		}
	}
}
