/*---------------------------------------------------------------------------------------------
 *  Copyright (c) Peter Bjorklund. All rights reserved. https://github.com/ecsreplicator
 *  Licensed under the MIT License. See LICENSE in the project root for license information.
 *--------------------------------------------------------------------------------------------*/

using System.Text;
using UnityEngine;

namespace EcsReplicator.Serialization.Unity
{
	public static class Helper
	{
		public static string OctetArrayToString(byte[] octets, uint count)
		{
			var sb = new StringBuilder();
			for (var i = 0; i < count; ++i)
			{
				if(i > 0)
				{
					sb.Append(",");
				}
				sb.Append($"{octets[i]:X}");
			}

			return sb.ToString();
		}
	}

	public static class TypeIdDiff
	{
		public static void Diff(
			byte[] from, uint fromCount,
			byte[] to, uint toCount,
			byte[] removed,
			out uint removedCount,
			byte[] added,
			out uint addedCount,
			byte[] sameArray,
			out uint sameCount)
		{
			int fromIndex = 0;
			int toIndex = 0;

			int removedIndex = 0;
			int addedIndex = 0;
			int sameIndex = 0;

			Debug.Log($"from: {Helper.OctetArrayToString(from, fromCount)}");
			Debug.Log($"to: {Helper.OctetArrayToString(to, toCount)}");

			while (fromIndex != fromCount && toIndex != toCount)
			{
				byte fromId = from[fromIndex];
				byte toId = to[toIndex];

				if(fromId == toId)
				{
					fromIndex++;
					toIndex++;

					sameArray[sameIndex++] = toId;

					continue;
				}

				if(toId > fromId)
				{
					// to is missing an ID
					removed[removedIndex++] = fromId;
					fromIndex++;
				}
				else
				{
					added[addedIndex++] = toId;
					toIndex++;
				}
			}

			// Are there extra in the to array. then they are added
			for (; toIndex < toCount; ++toIndex)
			{
				added[addedIndex++] = to[toIndex];
			}

			for (; fromIndex < fromCount; ++fromIndex)
			{
				removed[removedIndex++] = from[fromIndex];
			}

			removedCount = (uint)removedIndex;
			addedCount = (uint)addedIndex;
			sameCount = (uint)sameIndex;

			Debug.Log($"summary. added: {Helper.OctetArrayToString(added, addedCount)}, removed: {Helper.OctetArrayToString(removed, removedCount)}, same: {Helper.OctetArrayToString(sameArray, sameCount)}");
		}
	}
}