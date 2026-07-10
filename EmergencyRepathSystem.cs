using System.Collections.Generic;
using Game;
using Game.Common;
using Game.Pathfind;
using Game.Simulation;
using Game.Vehicles;
using Unity.Collections;
using Unity.Entities;

namespace EmergencyPriority
{
    // "Repath Guard Lite" — keeps responding emergency vehicles (CarFlags.Emergency: fire engines, ambulances,
    // police on emergency calls, evacuation buses) alive and moving through jams. Two verified vanilla gaps:
    //
    //   1. DESPAWN: when StuckMovingObjectSystem flags a responder PathFlags.Stuck, its AI system DELETES the
    //      vehicle outright (AmbulanceAISystem/FireEngineAISystem: IsStuck => AddComponent<Deleted>) and the rescue
    //      request enters exponential backoff while e.g. the building keeps burning.
    //   2. NO EN-ROUTE RE-ROUTING: pathfind cost includes live congestion (CarLane flow) ONLY at request time;
    //      the route is never re-evaluated afterwards, so a jam that forms after dispatch pins the vehicle forever.
    //
    // Fixes, both via a single main-thread PathOwner.m_State write the game itself consumes:
    //   * Stuck seen => clear Stuck, set Obsolete: the AI's delete branch never fires and VehicleUtils.RequireNewPath
    //     triggers a fresh, congestion-aware pathfind with the AI's own emergency weights (pure travel time,
    //     ignores heavy-traffic/combustion bans, may use bus lanes).
    //   * Blocked (vanilla test: Blocker.m_Blocker set, type != Temporary, m_MaxSpeed < 6 ≈ under ~2.6 m/s) for
    //     RerouteAfterSeconds => set Obsolete once per cooldown window, so the responder detours around the jam.
    //
    // Ordered after StuckMovingObjectSystem so a freshly set Stuck flag is cleared the same frame group where
    // possible (the AI consumes it within ~16 frames; interval 4 keeps the race window small). Watch-set of
    // Emergency-flagged cars refreshed every 64 frames — the full car scan is too costly per-tick, and a vehicle
    // must be blocked for minutes before vanilla would flag it, so joining the set up to 64 frames late is safe.
    public partial class EmergencyRepathSystem : GameSystemBase
    {
        private const uint kWatchRefreshFrames = 64;
        private const float kSimFramesPerSecond = 60f;

        private struct WatchState
        {
            public uint BlockedSince;   // frame the current continuous-blocked stretch started (0 = not blocked)
            public uint LastReroute;    // frame of the last Obsolete we issued for being blocked
        }

        private EntityQuery m_CarQuery;
        private SimulationSystem m_Sim;
        private readonly List<Entity> m_Watched = new List<Entity>();
        private readonly Dictionary<Entity, WatchState> m_State = new Dictionary<Entity, WatchState>();
        private uint m_LastWatchRefresh;
        private uint m_LastLog;

        // Session counters for the [SelfTest] log line.
        private int m_GuardedStuck;
        private int m_Reroutes;

        protected override void OnCreate()
        {
            base.OnCreate();
            m_Sim = World.GetOrCreateSystemManaged<SimulationSystem>();
            m_CarQuery = GetEntityQuery(new EntityQueryDesc
            {
                All = new[]
                {
                    ComponentType.ReadOnly<Car>(),
                    ComponentType.ReadOnly<Blocker>(),
                    ComponentType.ReadOnly<PathOwner>(),
                },
                None = new[]
                {
                    ComponentType.ReadOnly<OutOfControl>(),
                    ComponentType.ReadOnly<Deleted>(),
                    ComponentType.ReadOnly<Game.Tools.Temp>(),
                },
            });
            RequireForUpdate(m_CarQuery);
        }

        public override int GetUpdateInterval(SystemUpdatePhase phase) => 4;

        protected override void OnUpdate()
        {
            Setting s = Mod.ActiveSetting;
            if (s == null || !s.Enabled || (!s.DespawnGuard && !s.AutoReroute))
                return;

            uint frame = m_Sim.frameIndex;
            if (m_Watched.Count == 0 || frame - m_LastWatchRefresh >= kWatchRefreshFrames)
                RefreshWatchSet(frame);

            uint rerouteFrames = (uint)(s.RerouteAfterSeconds * kSimFramesPerSecond);
            uint cooldownFrames = rerouteFrames * 2;

            for (int i = m_Watched.Count - 1; i >= 0; i--)
            {
                Entity e = m_Watched[i];
                if (!EntityManager.Exists(e) || !EntityManager.HasComponent<Car>(e) || !EntityManager.HasComponent<PathOwner>(e))
                {
                    ForgetAt(i, e);
                    continue;
                }
                // Emergency flag drops when the mission phase changes (returning, parked) — stop touching it.
                if ((EntityManager.GetComponentData<Car>(e).m_Flags & CarFlags.Emergency) == 0)
                {
                    ForgetAt(i, e);
                    continue;
                }

                PathOwner po = EntityManager.GetComponentData<PathOwner>(e);
                bool pending = (po.m_State & PathFlags.Pending) != 0;

                // (1) Despawn guard: swap the give-up flag for a repath order before the AI's delete branch sees it.
                if (s.DespawnGuard && (po.m_State & PathFlags.Stuck) != 0)
                {
                    po.m_State &= ~PathFlags.Stuck;
                    if (!pending)
                        po.m_State |= PathFlags.Obsolete;
                    EntityManager.SetComponentData(e, po);
                    m_GuardedStuck++;
                    // A repath is now underway; restart the blocked clock.
                    m_State[e] = new WatchState { BlockedSince = 0, LastReroute = frame };
                    continue;
                }

                // (2) Blocked-too-long re-route (vanilla blocked test, StuckMovingObjectSystem.cs).
                if (!s.AutoReroute || !EntityManager.HasComponent<Blocker>(e))
                    continue;
                Blocker blk = EntityManager.GetComponentData<Blocker>(e);
                bool blocked = blk.m_Blocker != Entity.Null && blk.m_Type != BlockerType.Temporary && blk.m_MaxSpeed < 6;

                m_State.TryGetValue(e, out WatchState st);
                if (!blocked)
                {
                    if (st.BlockedSince != 0)
                    {
                        st.BlockedSince = 0;
                        m_State[e] = st;
                    }
                    continue;
                }
                if (st.BlockedSince == 0)
                {
                    st.BlockedSince = frame;
                    m_State[e] = st;
                    continue;
                }
                if (frame - st.BlockedSince >= rerouteFrames && frame - st.LastReroute >= cooldownFrames
                    && !pending && (po.m_State & (PathFlags.Obsolete | PathFlags.Failed)) == 0)
                {
                    po.m_State |= PathFlags.Obsolete; // RequireNewPath => fresh congestion-aware emergency pathfind
                    EntityManager.SetComponentData(e, po);
                    st.LastReroute = frame;
                    st.BlockedSince = 0;
                    m_State[e] = st;
                    m_Reroutes++;
                }
            }

            // Self-test heartbeat (~every 16384 frames ≈ quarter of an in-game day): always logs so the next launch
            // can confirm the system is alive and tracking responders even if nobody got stuck. watched>0 proves
            // the watch set is finding fire/ambulance/police vehicles on emergency calls.
            if (frame - m_LastLog >= 16384)
            {
                m_LastLog = frame;
                Mod.log.Info($"[SelfTest] emergencyRepath status: enabled={s.Enabled} guard={s.DespawnGuard} reroute={s.AutoReroute} watchedResponders={m_Watched.Count} guardedStuckTotal={m_GuardedStuck} reroutesTotal={m_Reroutes}");
            }
        }

        private void RefreshWatchSet(uint frame)
        {
            m_LastWatchRefresh = frame;
            m_Watched.Clear();
            NativeArray<Entity> cars = m_CarQuery.ToEntityArray(Allocator.Temp);
            for (int i = 0; i < cars.Length; i++)
            {
                if ((EntityManager.GetComponentData<Car>(cars[i]).m_Flags & CarFlags.Emergency) != 0)
                    m_Watched.Add(cars[i]);
            }
            cars.Dispose();
            // Prune stale timer state for entities that left the watch set.
            if (m_State.Count > m_Watched.Count * 4 + 64)
            {
                var keep = new HashSet<Entity>(m_Watched);
                var stale = new List<Entity>();
                foreach (var kv in m_State)
                    if (!keep.Contains(kv.Key))
                        stale.Add(kv.Key);
                for (int i = 0; i < stale.Count; i++)
                    m_State.Remove(stale[i]);
            }
        }

        private void ForgetAt(int index, Entity e)
        {
            m_Watched.RemoveAt(index);
            m_State.Remove(e);
        }
    }
}
