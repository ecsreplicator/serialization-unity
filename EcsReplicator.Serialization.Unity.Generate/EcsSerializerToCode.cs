/*---------------------------------------------------------------------------------------------
 *  Copyright (c) Peter Bjorklund. All rights reserved.
 *  Licensed under the MIT License. See LICENSE in the project root for license information.
 *--------------------------------------------------------------------------------------------*/

using System.Linq;
using EcsReplicator.Attributes.Scan;
using Mono.CecilEx;
using Piot.CSharpSourceGenerator;


namespace EcsReplicator.Serialization.Unity.Generate
{
	public static class EcsSerializerToCode
	{
		public static bool IsEnableable(TypeDefinition componentDataType)
		{
			return componentDataType.Interfaces.Any(i =>
				i.InterfaceType.FullName == "Unity.Entities.IEnableableComponent");
		}

		public static void DeleteOrDisableTypeIds(IClassScope sb, DataStructInfo[] sortedMetas)
		{
			using var m =
				sb.PublicStaticMethod(
					"void DeleteOrDisableTypeIds(byte[] typeIds, uint count, EntityManager entityManager, Entity entity, NativeArray<ComponentType> targetComponentTypes)");

			m.Line("  var componentTypeCount = 0;");

			using (var c = m.For("var i = 0; i < count; ++i"))
			{
				using (var s = c.Switch("typeIds[i]"))
				{
					foreach (var meta in sortedMetas)
					{
						using var typeIdCase = s.Case("" + meta.UniqueId);
						if(IsEnableable(meta.TypeDefinition))
						{
							using (var hasComponentIt = typeIdCase
								       .If("entityManager.HasComponent<Example.Scripts.Downed>(entity)").Consequence)
							{
								hasComponentIt.Lines(@"
										Debug.Log($""disabling " + meta.TypeDefinition.FullName + @" "");
										entityManager.SetComponentEnabled<" + meta.TypeDefinition.FullName +
								                     @">(entity, false);");
							}

							typeIdCase.Line(@"break;");
						}
						else
						{
							using (var ifOverComponentLength =
							       typeIdCase.If("componentTypeCount >= targetComponentTypes.Length").Consequence)
							{
								ifOverComponentLength.Line(@"throw new Exception(""target buffer too small"");");
							}

							typeIdCase.Line(
								@"targetComponentTypes[componentTypeCount++] = typeof(" +
								meta.TypeDefinition.FullName + ");");
							typeIdCase.Line(@"break;");
						}
					}
				}
			}

			m.Lines(@"
				var componentTypesToRemove = new ComponentType[componentTypeCount];
				Array.Copy(targetComponentTypes.ToArray(), componentTypesToRemove, componentTypeCount);
				var componentTypeSetToRemove = new ComponentTypeSet(componentTypesToRemove);
				entityManager.RemoveComponent(entity, componentTypeSetToRemove);
			");
		}

		public static void TypeIdsToComponents(IClassScope sb, DataStructInfo[] sortedMetas)
		{
			using var m = sb.PublicStaticMethod(
				@"ComponentType[] TypeIdsToComponentTypes(byte[] typeIds, uint count)", "TypeIdsToComponentTypes");

			m.Line("var componentTypes = new ComponentType[count];");

			using (var c = m.For("var i = 0; i < count; ++i"))
			{
				using (var b = c.Block("componentTypes[i] = typeIds[i] switch"))
				{
					foreach (var meta in sortedMetas)
					{
						b.Line(@$"{meta.UniqueId} => typeof({meta.TypeDefinition.FullName}),");
					}

					b.Line(
						@"_ => throw new Exception($""unknown component typeID during ComponentType lookup {typeIds[i]:X}"")");
				}

				c.Line(";");

				c.Line(@"Debug.Log($""converted type ID); {typeIds[i]} to {componentTypes[i].GetType().FullName}"");");
			}

			m.Line("return componentTypes;");
		}


		public static void EnableTypeIds(IClassScope sb, DataStructInfo[] sortedMetas)
		{
			using var m =
				sb.PublicStaticMethod(
					@"void EnableTypeIds(EntityManager entityManager, Entity entity, byte[] tempAdded, uint foundAddedCount)");

			using var f = m.ForEach("var componentTypeId in tempAdded");
			using var s = f.Switch("componentTypeId");

			foreach (var meta in sortedMetas)
			{
				if(!IsEnableable(meta.TypeDefinition))
				{
					continue;
				}

				using var c = s.Case("" + meta.UniqueId);
				c.Lines(@"
								entityManager.SetComponentEnabled<" + meta.TypeDefinition.FullName +
				        @">(entity, true);
								break;
							");
			}
		}


		public static void DeserializeFullGhostStateGenerated(IClassScope sb, DataStructInfo[] sortedMetas)
		{
			using var m =
				sb.PublicStaticMethod(
					@"unsafe void DeserializeFullGhostStateGenerated(EntityManager entityManager, ref DeserializeWorld world, byte* source, int size)",
					"Deserialize");

			m.Lines(@"
	            var tempIdTarget = new NativeArray<byte>(256, Allocator.Temp);
				var tempComponentTypes = new NativeArray<ComponentType>(64, Allocator.Temp);
	            var tempAdded = new byte[256];
	            var tempRemoved = new byte[256];
	            var tempSame = new byte[256];
	            var tempComponentTypeIdTarget = new byte[256];
			");

			m.Lines("byte* p = source;");
			
			m.Lines("world.deserializeId++;");

			using (var w = m.While("true"))
			{
				w.Lines(@"	
	                ushort entityId = *(ushort*)p;
	                p += sizeof(ushort);
					Debug.Log($""found entity {entityId:X}"");
				");

				using (var b = w.If("entityId == 0xffff").Consequence)
				{
					b.Line("break;");
				}

				w.Line("int deserializedTypeIdCount = 0;");

				using (var p = w.While("*p != 0xff"))
				{
					p.Lines(@"
						Debug.Log($""read component ID {*p:X}"");
						tempComponentTypeIdTarget[deserializedTypeIdCount++] = *p++;
					");
				}

				w.Lines(@"
				p++;
                Debug.Log($""end of reading component IDs. found count: {deserializedTypeIdCount}"");
                var hasEntityAlready = world.entityLookup.TryGetValue(entityId, out var entityInfo);

				Entity entity;
				");

				using (var ifscope = w.If("hasEntityAlready"))
				{
					using (var h = ifscope.Consequence)
					{
						h.Lines(@"
                    entity = entityInfo.entity;
					Debug.Log($""we had this entity already"");
                    var existingComponentTypes = entityManager.GetComponentTypes(entity, Allocator.Temp);

                    var componentTypeIdCount = ComponentTypesToIds(existingComponentTypes, ref tempIdTarget);
	                var sortedComponentTypeIdArray = new byte[componentTypeIdCount];
	                NativeArray<byte>.Copy(tempIdTarget, sortedComponentTypeIdArray, (int)componentTypeIdCount);

                    Array.Sort(sortedComponentTypeIdArray);

                    Debug.Log($""entity already has {sortedComponentTypeIdArray}"");
                 
                    TypeIdDiff.Diff(sortedComponentTypeIdArray, (uint)sortedComponentTypeIdArray.Length,
                        tempComponentTypeIdTarget, (uint)deserializedTypeIdCount,
                        tempRemoved, out var foundRemovedCount,
                        tempAdded, out var foundAddedCount,
                        tempSame, out var foundSameCount);
					");

						using (var fr = h.If("foundRemovedCount > 0").Consequence)
						{
							fr.Lines(@"
							DeleteOrDisableTypeIds(tempRemoved, foundRemovedCount, entityManager, entity, tempComponentTypes);
						");
						}

						using (var fa = h.If("foundAddedCount > 0").Consequence)
						{
							fa.Lines(@"
                        var componentTypesToAdd = TypeIdsToComponentTypes(tempAdded, foundAddedCount);
                        var componentTypeSetToAdd = new ComponentTypeSet(componentTypesToAdd);
                        entityManager.AddComponent(entity, componentTypeSetToAdd);
						EnableTypeIds(entityManager, entity, tempAdded, foundAddedCount);
						");
						}

						h.Lines(@"EnableTypeIds(entityManager, entity, tempSame, foundSameCount);");

						h.Lines(@"
							entityInfo.deserializeCounter = world.deserializeId;
							world.entityLookup[entityId] = entityInfo;", "refresh counter so it isn't deleted");

					}

					using (var e = ifscope.CreateAlternative())
					{
						e.Lines(@"
						Debug.Log($""this is a new entity for us"");
	                    var componentTypes = TypeIdsToComponentTypes(tempComponentTypeIdTarget, (uint)deserializedTypeIdCount);
	                    entity = entityManager.CreateEntity(componentTypes);
						world.entityLookup.Add(entityId, new DeserializeEntityInfo { entity = entity, deserializeCounter = world.deserializeId });
					");
					}
				}

				using (var i = w.For("var i = 0; i < deserializedTypeIdCount; ++i"))
				{
					using (var s = i.Switch("tempComponentTypeIdTarget[i]"))
					{
						foreach (var meta in sortedMetas)
						{
							using var c = s.Case("" + meta.UniqueId);
							c.Lines(@"entityManager.SetComponentData(entity, *(" +
							        meta.TypeDefinition.FullName +
							        @"*) p);
									    p += sizeof(" + meta.TypeDefinition.FullName + @");
										Debug.Log($""wrote component data " + meta.TypeDefinition.FullName +
							        " with size {sizeof(" + meta.TypeDefinition.FullName + @" )} "" );

										break;
								");
						}
					}
				}
			}

			m.Lines(@"
					var tempDeletedEntities = new NativeArray<Entity>(256, Allocator.Temp);
					var deletedEntityCount = 0;
					var tempDeletedEntityIds = new NativeArray<ushort>(256, Allocator.Temp);
			");
			
			using (var fe = m.ForEach("var entityInfoPair in world.entityLookup"))
			{
				using (var ifd = fe.If("entityInfoPair.Value.deserializeCounter != world.deserializeId",
					       "If it wasn't in the full deserialization, schedule for deletion").Consequence)
				{
					ifd.Lines(@"
							tempDeletedEntityIds[deletedEntityCount] = entityInfoPair.Key;
							tempDeletedEntities[deletedEntityCount] = entityInfoPair.Value.entity;
							deletedEntityCount++;
					");
				}
			}

			using (var ifdc = m.If("deletedEntityCount > 0").Consequence)
			{
				ifdc.Lines(@"
                var deletedEntitiesSlice = new NativeSlice<Entity>(tempDeletedEntities, 0, deletedEntityCount); 
                Debug.Log($""tempDeleted entities {deletedEntitiesSlice.Length} {deletedEntityCount}"");
                entityManager.DestroyEntity(deletedEntitiesSlice);
				");

				using (var deli = ifdc.For("var i = 0; i < deletedEntityCount; ++i"))
				{
					deli.Line(@"world.entityLookup.Remove(tempDeletedEntityIds[i]);");
				}
			}
		}

		public static void GenerateTypesToIdsFilterOutDisabled(IClassScope sb,
			DataStructInfo[] sortedMetas)
		{
			using var m = sb.PublicStaticMethod(
				"uint GenerateTypesToIdsFilterOutDisabled(NativeArray<ComponentType> componentTypes, ref NativeArray<byte> targetArray, EntityManager entityManager, Entity entity)",
				"GenerateTypesToIdsFilterOutDisabled");

			m.Line("var foundCount = 0;");

			using (var c = m.ForEach("var componentType in componentTypes", "iterate componentTypes"))
			{
				foreach (var meta in sortedMetas)
				{
					if(IsEnableable(meta.TypeDefinition))
					{
						c.Line(@"
                if (componentType == typeof(" + meta.TypeDefinition.FullName +
						       @") && entityManager.IsComponentEnabled<" + meta.TypeDefinition.FullName +
						       ">(entity))");
					}
					else
					{
						c.Line(@"if (componentType == typeof(" + meta.TypeDefinition.FullName + @"))");
					}

					using var b = c.Block();
					c.Lines(@"
				                  targetArray[foundCount++] = " + meta.UniqueId + @";
								 Debug.Log(""converting component type " + meta.TypeDefinition.FullName +
					        " to " +
					        meta.UniqueId + @" "");
								continue;
							");
				}

				c.Line(@"Debug.Log($""Skipping component type {componentType.GetManagedType().FullName}"");");
			}

			m.Line("return (uint) foundCount;");
		}


		public static void GenerateTypesToIds(IClassScope sb, DataStructInfo[] sortedMetas)
		{
			using var m = sb.PublicStaticMethod(
				@"uint ComponentTypesToIds(NativeArray<ComponentType> componentTypes, ref NativeArray<byte> targetArray)",
				"ComponentTypesToIds");

			m.Line("var foundCount = 0;");

			using (var c = m.ForEach("var componentType in componentTypes", "componentTypes"))
			{
				foreach (var meta in sortedMetas)
				{
					using var i = c.If(@"componentType == typeof(" + meta.TypeDefinition.FullName +
					                   @")").Consequence;
					i.Lines(@"
									targetArray[foundCount++] = " + meta.UniqueId + @";
									Debug.Log(""converting component type " + meta.TypeDefinition.FullName +
					        " to " + meta.UniqueId + @" "");
									continue;");
				}

				c.Line(@"Debug.Log($""Skipping component type {componentType.GetManagedType().FullName}"");");
			}

			m.Line("return (uint) foundCount;");
		}

		public static void SerializeFullGhostStateGenerated(IClassScope scope, DataStructInfo[] sortedMetas)
		{
			using var m = scope.PublicStaticMethod(
				"unsafe int SerializeFullGhostStateGenerated(EntityManager entityManager, byte* _target, int size, SerializeWorld world, Entity[] entities)",
				"Dump");

			m.Lines(@"
					var tempIdTarget = new NativeArray<byte>(256, Allocator.Temp);
					var p = _target;
					var lastP = _target + size;
					Entity entity;
				");


			using (var l = m.For("var i = 0; i < entities.Length; ++i", "iterate entities"))
			{
				l.Lines(
					@"entity = entities[i];
						Debug.Log($""Entity: {entity}"");"
				);

				using (var r = l.If("p + sizeof(ushort) >= lastP").Consequence)
				{
					r.Line("return -1;");
				}

				l.Line(@"var foundPreviousEntity = world.entityLookup.TryGetValue(entity, out var entityId);");

				using (var a = l.If("!foundPreviousEntity").Consequence)
				{
					a.Line("entityId = world.lastEntityId++;");

					a.Line("world.entityLookup.Add(entity, entityId);");
				}

				l.Lines(@"
						*(ushort*) p = entityId;
						p += sizeof(ushort);
						var componentTypes = entityManager.GetComponentTypes(entity, Allocator.Temp);
		                var componentTypeIdCount = GenerateTypesToIdsFilterOutDisabled(componentTypes, ref tempIdTarget, entityManager, entity);
						var sortedComponentTypeIdArray = new byte[componentTypeIdCount];
		                NativeArray<byte>.Copy(tempIdTarget, sortedComponentTypeIdArray, (int)componentTypeIdCount);
		                Array.Sort(sortedComponentTypeIdArray);");

				using (var c = l.ForEach("var componentTypeId in sortedComponentTypeIdArray",
					       "sortedComponentTypeIdArray"))
				{
					c.Line("*p++ = componentTypeId;");
				}

				l.Line("*p++ = 0xff;");

				using (var d = l.ForEach("var componentTypeId in sortedComponentTypeIdArray",
					       "sortedComponentTypeIdArray"))
				{
					using (var x = d.Switch("componentTypeId", "switch componentTypeId"))
					{
						foreach (var meta in sortedMetas)
						{
							using var c = x.Case("" + meta.UniqueId, meta.TypeDefinition.FullName);
							c.Lines(@"   *(" + meta.TypeDefinition.FullName +
							        @"*) p = entityManager.GetComponentData<" +
							        meta.TypeDefinition.FullName + @">(entity);
										p += sizeof(" + meta.TypeDefinition.FullName + @");
								break;");
						}

						using (var def = x.Default())
						{
							def.Line(
								@"throw new Exception($"" unknown component type ID {componentTypeId:X} during serialization "");");
						}
					}
				}
			}

			m.Lines(@"
					*(ushort*)p = 0xffff; // End of entities
					p += sizeof(ushort);

					return (int)(p-_target);
			");
		}


		public static string Generate(DataStructInfo[] sortedMetas)
		{
			var sourceGenerator = new SourceGenerator();

			using var rootScope = new RootScope(sourceGenerator);

			rootScope.Usings(@"
								using System;
								using Unity.Entities;
								using UnityEngine;
								using Unity.Collections;

								using EcsReplicator.Serialization.Unity;
			");

			using (var generatedNamespace = rootScope.Namespace("Piot.EcsReplicator.Unity"))
			{
				using (var generatedStaticClass =
				       generatedNamespace.PublicStaticClass("SerializerFullGhostState"))
				{
					GenerateTypesToIds(generatedStaticClass, sortedMetas);
					TypeIdsToComponents(generatedStaticClass, sortedMetas);
					DeleteOrDisableTypeIds(generatedStaticClass, sortedMetas);
					EnableTypeIds(generatedStaticClass, sortedMetas);
					GenerateTypesToIdsFilterOutDisabled(generatedStaticClass, sortedMetas);

					SerializeFullGhostStateGenerated(generatedStaticClass, sortedMetas);
					DeserializeFullGhostStateGenerated(generatedStaticClass, sortedMetas);
				}
			}

			return sourceGenerator.String();
		}
	}
}