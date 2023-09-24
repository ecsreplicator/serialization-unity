/*---------------------------------------------------------------------------------------------
 *  Copyright (c) Peter Bjorklund. All rights reserved. https://github.com/ecsreplicator
 *  Licensed under the MIT License. See LICENSE in the project root for license information.
 *--------------------------------------------------------------------------------------------*/

using Unity.Collections;
using Unity.Entities;

namespace EcsReplicator.Serialization.Unity
{
	public struct DeserializeEntityInfo
	{
		public Entity entity;
		public byte deserializeCounter;
	}

	public struct DeserializeWorld : IComponentData
	{
		public NativeHashMap<ushort, DeserializeEntityInfo> entityLookup;
		public byte deserializeId;
	}


	public struct SerializeWorld : IComponentData
	{
		public NativeHashMap<Entity, ushort> entityLookup;
		public ushort lastEntityId;
	}
}