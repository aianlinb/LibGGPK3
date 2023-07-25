/***** BEGIN LICENSE BLOCK *****
 * Version: MPL 1.1/GPL 2.0/LGPL 2.1
 *
 * The contents of this file are subject to the Mozilla Public License Version
 * 1.1 (the "License"); you may not use this file except in compliance with
 * the License. You may obtain a copy of the License at
 * http://www.mozilla.org/MPL/
 *
 * Software distributed under the License is distributed on an "AS IS" basis,
 * WITHOUT WARRANTY OF ANY KIND, either express or implied. See the License
 * for the specific language governing rights and limitations under the
 * License.
 *
 * The Original Code is HashTableHashing.MurmurHash2.
 *
 * The Initial Developer of the Original Code is
 * Davy Landman.
 * Portions created by the Initial Developer are Copyright (C) 2009
 * the Initial Developer. All Rights Reserved.
 *
 * Contributor(s):
 *
 *
 * Alternatively, the contents of this file may be used under the terms of
 * either the GNU General Public License Version 2 or later (the "GPL"), or
 * the GNU Lesser General Public License Version 2.1 or later (the "LGPL"),
 * in which case the provisions of the GPL or the LGPL are applicable instead
 * of those above. If you wish to allow use of your version of this file only
 * under the terms of either the GPL or the LGPL, and not to allow others to
 * use your version of this file under the terms of the MPL, indicate your
 * decision by deleting the provisions above and replace them with the notice
 * and other provisions required by the GPL or the LGPL. If you do not delete
 * the provisions above, a recipient may use your version of this file under
 * the terms of any one of the MPL, the GPL or the LGPL.
 *
 * ***** END LICENSE BLOCK ***** 
 
 Below code was modified by aianlinb  2022/2/6
 */
using System;

internal static unsafe class MurmurHash2Unsafe {
	const UInt32 m = 0x5BD1E995;
	const Int32 r = 24;

	public static UInt32 Hash(String name, UInt32 seed = 0xC58F1A7B) {
		fixed (char* p = name)
			return Hash(new ReadOnlySpan<Byte>(p, name.Length * 2), seed);
	}

	public static UInt32 Hash(ReadOnlySpan<Byte> data, UInt32 seed = 0xC58F1A7B) {
		Int32 length = data.Length;

		UInt32 h = seed ^ (UInt32)length;
		Int32 remainingBytes = length & 3; // mod 4
		Int32 numberOfLoops = length >> 2; // div 4
		fixed (byte* firstByte = data) {
			UInt32* realData = (UInt32*)firstByte;
			while (numberOfLoops-- != 0) {
				UInt32 k = *realData++;
				k *= m;
				k ^= k >> r;
				k *= m;

				h *= m;
				h ^= k;
			}
			switch (remainingBytes) {
				case 3:
					h ^= (UInt16)(*realData);
					h ^= ((UInt32)(*(((Byte*)realData) + 2))) << 16;
					h *= m;
					break;
				case 2:
					h ^= (UInt16)(*realData);
					h *= m;
					break;
				case 1:
					h ^= *(Byte*)realData;
					h *= m;
					break;
				default:
					break;
			}
		}

		// Do a few final mixes of the hash to ensure the last few
		// bytes are well-incorporated.
		h ^= h >> 13;
		h *= m;
		h ^= h >> 15;
		return h;
	}
}