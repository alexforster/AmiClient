/* Copyright © 2019 Alex Forster. All rights reserved.
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

namespace Ami.ByteArrayExtensions
{
	using System;
	using System.Diagnostics;
	using System.Collections.Generic;

	internal static class ByteArrayExtensions
	{
		[DebuggerStepThrough]
		public static Byte[] Prepend(this Byte[] @this, Byte[] items)
		{
			var result = new Byte[@this.Length + items.Length];

			Buffer.BlockCopy(items, 0, result, 0, items.Length);
			Buffer.BlockCopy(@this, 0, result, items.Length, @this.Length);

			return result;
		}

		[DebuggerStepThrough]
		public static Byte[] Append(this Byte[] @this, Byte[] items)
		{
			var result = new Byte[@this.Length + items.Length];

			Buffer.BlockCopy(@this, 0, result, 0, @this.Length);
			Buffer.BlockCopy(items, 0, result, @this.Length, items.Length);

			return result;
		}

		[DebuggerStepThrough]
		public static Byte[] Slice(this Byte[] @this, Int32 start)
		{
			var _start = (start < 0) ? (@this.Length + start) : (start);
			var _end = (@this.Length);

			return @this.Slice(_start, _end);
		}

		[DebuggerStepThrough]
		public static Byte[] Slice(this Byte[] @this, Int32 start, Int32 end)
		{
			var _start = (start < 0) ? (@this.Length + start) : (start);
			var _end = (end < 0) ? (@this.Length + end) : (end);

			if(_start < 0 || @this.Length < _start)
			{
				throw new ArgumentOutOfRangeException(nameof(start));
			}

			if(_end < _start || @this.Length < _end)
			{
				throw new ArgumentOutOfRangeException(nameof(end));
			}

			var result = new Byte[_end - _start];

			Buffer.BlockCopy(@this, _start, result, 0, result.Length);

			return result;
		}

		[DebuggerStepThrough]
		public static Int32 Find(this Byte[] @this, Byte[] needle, Int32 start = 0)
		{
			var _start = (start < 0) ? (@this.Length + start) : (start);
			var _end = (@this.Length);

			return @this.Find(needle, _start, _end);
		}

		[DebuggerStepThrough]
		public static Int32 Find(this Byte[] @this, Byte[] needle, Int32 start, Int32 end)
		{
			var _start = (start < 0) ? (@this.Length + start) : (start);
			var _end = (end < 0) ? (@this.Length + end) : (end);

			var needlePos = 0;

			for(var i = _start; i < _end; i++)
			{
				if(@this[i] == needle[needlePos])
				{
					if(++needlePos == needle.Length)
					{
						return i - needlePos + 1;
					}
				}
				else
				{
					i -= needlePos;
					needlePos = 0;
				}
			}

			return -1;
		}

		[DebuggerStepThrough]
		public static Int32[] FindAll(this Byte[] @this, Byte[] needle, Int32 start = 0)
		{
			var _start = (start < 0) ? (@this.Length + start) : (start);
			var _end = (@this.Length);

			return @this.FindAll(needle, _start, _end);
		}

		[DebuggerStepThrough]
		public static Int32[] FindAll(this Byte[] @this, Byte[] needle, Int32 start, Int32 end)
		{
			var _start = (start < 0) ? (@this.Length + start) : (start);
			var _end = (end < 0) ? (@this.Length + end) : (end);

			var matches = new List<Int32>();

			var needlePos = 0;

			for(var i = _start; i < _end; i++)
			{
				if(@this[i] == needle[needlePos])
				{
					if(++needlePos == needle.Length)
					{
						matches.Add(i - needlePos + 1);
						needlePos = 0;
					}
				}
				else
				{
					i -= needlePos;
					needlePos = 0;
				}
			}

			return matches.ToArray();
		}
	}
}
