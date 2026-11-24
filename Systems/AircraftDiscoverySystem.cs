using System;
using System.Collections.Generic;
using Game;
using Game.Objects;
using Game.Prefabs;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

namespace CS2FlightTakeover.Systems
{
    // Mirrors the verified aircraft detection strategy of NyokoDev/FlightTrackerCS2.
    // No aircraft simulation components are written here.
    public sealed partial class AircraftDiscoverySystem : GameSystemBase
    {
        public readonly struct Entry
        {
            public readonly Entity Entity;
            public readonly float3 Position;
            public readonly string Kind;
            public readonly float Speed;

            public Entry(Entity entity, float3 position, string kind, float speed)
            {
                Entity = entity;
                Position = position;
                Kind = kind;
                Speed = speed;
            }
        }

        private EntityQuery _query;
        private PrefabSystem _prefabSystem;
        private readonly List<Entry> _entries = new List<Entry>();
        private int _updateCounter;
        public IReadOnlyList<Entry> Entries => _entries;

        protected override void OnCreate()
        {
            base.OnCreate();
            _prefabSystem = World.GetOrCreateSystemManaged<PrefabSystem>();
            _query = GetEntityQuery(
                ComponentType.ReadOnly<PrefabRef>(),
                ComponentType.ReadOnly<Transform>());
        }

        protected override void OnUpdate()
        {
            // Reference mod scans every 30 updates; avoid a full prefab scan each frame.
            if (++_updateCounter < 30) return;
            _updateCounter = 0;
            Refresh();
        }

        private void Refresh()
        {
            _entries.Clear();
            using (NativeArray<Entity> entities = _query.ToEntityArray(Allocator.Temp))
            using (NativeArray<PrefabRef> prefabs = _query.ToComponentDataArray<PrefabRef>(Allocator.Temp))
            using (NativeArray<Transform> transforms = _query.ToComponentDataArray<Transform>(Allocator.Temp))
            {
                for (int i = 0; i < entities.Length; i++)
                {
                    var prefabEntity = prefabs[i].m_Prefab;
                    if (prefabEntity == Entity.Null) continue;
                    if (!_prefabSystem.TryGetPrefab(prefabEntity, out PrefabBase prefab)) continue;
                    if (!(prefab is AirplanePrefab airplane)) continue;

                    var entity = entities[i];
                    var position = transforms[i].m_Position;
                    float speed = 0;
                    if (EntityManager.HasComponent<Moving>(entity))
                    {
                        speed = math.length(EntityManager.GetComponentData<Moving>(entity).m_Velocity);
                    }

                    string kind = string.IsNullOrWhiteSpace(airplane.name) ? "Airplane" : airplane.name;
                    _entries.Add(new Entry(entity, position, kind, speed));
                }
            }
            _entries.Sort((a, b) => string.Compare(a.Kind, b.Kind, StringComparison.OrdinalIgnoreCase));
        }
    }
}
