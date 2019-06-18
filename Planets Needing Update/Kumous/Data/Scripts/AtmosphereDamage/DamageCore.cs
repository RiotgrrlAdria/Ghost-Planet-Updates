using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Sandbox.Game.Entities;
using Sandbox.ModAPI;
using VRage.Game;
using VRage.Game.Components;
using VRage.Game.ModAPI;
using VRage.ModAPI;
using VRage.Utils;
using VRageMath;

/*
 *  Mod by Rexxar. Feel free to steal, hack, reuse, send money, etc.
 */

namespace AtmosphericDamage
{
    [MySessionComponentDescriptor(MyUpdateOrder.BeforeSimulation)]
    public class DamageCore : MySessionComponentBase
    {
        /////////////////////CHANGE THESE FOR EACH PLANET////////////////////////////
        
        private const string PLANET_NAME = "Kumous"; //this mod targets planet Kumous
        private const int UPDATE_RATE = 200; //damage will apply every 200 frames
        private const float LARGE_SHIP_DAMAGE = 2.5f; //applies 2.5 damage to each block per update
        private const float SMALL_SHIP_DAMAGE = 1.25f;
        private const string DAMAGE_STRING = PLANET_NAME + "Atmosphere";
        private const bool DAMAGE_PLAYERS = false; //set true to apply atmospheric damage to players
        private const float PLAYER_DAMAGE_AMOUNT = 0.1f;

        /////////////////////////////////////////////////////////////////////////////

        private readonly Random _random = new Random();
        private bool _init;
        private HashSet<MyPlanet> _planets = new HashSet<MyPlanet>();
        private int _updateCount = 0;
        private MyStringHash _damageHash;
        private bool _processing;
        private Queue<Action> _actionQueue = new Queue<Action>();
        private int _actionsPerTick = 0;

        public override void UpdateBeforeSimulation()
        {
            try
            {
                if (MyAPIGateway.Session == null)
                    return;

                //server only please
                if (!(MyAPIGateway.Session.OnlineMode == MyOnlineModeEnum.OFFLINE || MyAPIGateway.Multiplayer.IsServer))
                    return;

                if (!_init)
                    Initialize();
                
                ProcessQueue();

                //update our list of planets every 10 seconds in case people paste new planets
                if (++_updateCount % 600 == 0)
                {
                    _planets.Clear();
                    var entities = new HashSet<IMyEntity>();
                    MyAPIGateway.Entities.GetEntities(entities);
                    foreach (var entity in entities)
                    {
                        var planet = entity as MyPlanet;
                        if (planet == null)
                            continue;
                        if (planet.StorageName.StartsWith(PLANET_NAME))
                            _planets.Add(planet);
                    }
                }

                if (_updateCount % UPDATE_RATE != 0)
                    return;

                if (_processing) //worker thread is busy
                    return;

                _processing = true;
                MyAPIGateway.Parallel.Start(ProcessDamage);
            }
            catch
            {
                //suppress all errors
            }
        }

        private void ProcessDamage()
        {
            try
            {
                foreach (var planet in _planets)
                {
                    var sphere = new BoundingSphereD(planet.PositionComp.GetPosition(), planet.AverageRadius + planet.AtmosphereAltitude);

                    var topEntities = MyAPIGateway.Entities.GetTopMostEntitiesInSphere(ref sphere);
                    foreach (var entity in topEntities)
                    {
                        var grid = entity as IMyCubeGrid;
                        if (grid?.Physics != null)
                        {

                            if (grid.Closed || grid.MarkedForClose)
                                continue;

                            var blocks = new List<IMySlimBlock>();
                            grid.GetBlocks(blocks);

                            for (int i = 0; i < blocks.Count * 0.3; i++)
                            {
                                var block = GetRandomExteriorBlock(grid, blocks);
                                QueueInvoke(() => block?.DoDamage(grid.GridSizeEnum == MyCubeSize.Small ? SMALL_SHIP_DAMAGE : LARGE_SHIP_DAMAGE, _damageHash, true));
                            }

                            continue;
                        }

                        var floating = entity as IMyFloatingObject;
                        if (floating != null)
                        {
                            if (floating.Closed || floating.MarkedForClose)
                                continue;

                            QueueInvoke(() => floating.DoDamage(SMALL_SHIP_DAMAGE, _damageHash, true));

                            continue;
                        }

                        if (DAMAGE_PLAYERS)
                        {
                            var character = entity as IMyCharacter;
                            if (character != null)
                            {
                                if (character.Closed || character.MarkedForClose)
                                    continue;

                                QueueInvoke(() => character.DoDamage(PLAYER_DAMAGE_AMOUNT, _damageHash, true));

                                continue;
                            }
                        }
                    }
                }
            }
            catch
            {
                //don't care
            }
            finally
            {
                _actionsPerTick = _actionQueue.Count / UPDATE_RATE + 1;
                _processing = false;
            }
        }

        private void Initialize()
        {
            _init = true;

            _damageHash = MyStringHash.GetOrCompute(DAMAGE_STRING);

            //initialize our planet list
            var entities = new HashSet<IMyEntity>();
            MyAPIGateway.Entities.GetEntities(entities);
            foreach (var entity in entities)
            {
                var planet = entity as MyPlanet;
                if (planet == null)
                    continue;
                if (planet.StorageName.StartsWith(PLANET_NAME))
                    _planets.Add(planet);
            }
        }

        private IMySlimBlock GetRandomExteriorBlock(IMyCubeGrid grid, List<IMySlimBlock> blocks )
        {
            Vector3D posInt = grid.GridIntegerToWorld(blocks.GetRandomItemFromList().Position);
            Vector3D posExt = RandomPositionFromPoint(posInt, grid.WorldAABB.HalfExtents.Length());
            Vector3I? blockPos = grid.RayCastBlocks(posExt, posInt);
            return blockPos.HasValue ? grid.GetCubeBlock(blockPos.Value) : null;
        }

        private Vector3D RandomPositionFromPoint(Vector3D start, double distance)
        {
            var randomPoint = new Vector3D(_random.Next(-100000, 100000), _random.Next(-100000, 100000), _random.Next(-100000, 100000));
            Vector3D directionNormal = randomPoint - start;
            directionNormal.Normalize();
            directionNormal = directionNormal * (long)distance + start;
            return directionNormal;
        }

        //spread our invoke queue over many updates to avoid lag spikes
        private void ProcessQueue()
        {
            if (_actionQueue.Count == 0)
                return;

            for (int i = 0; i < _actionsPerTick; i++)
            {
                Action action;
                if (!_actionQueue.TryDequeue(out action))
                    return;

                SafeInvoke(action);
            }
        }

        private void QueueInvoke(Action action)
        {
            _actionQueue.Enqueue(action);
        }

        //wrap invoke in try/catch so we don't crash on unexpected error
        //what we're doing isn't critical, so don't bother logging the errors
        private void SafeInvoke(Action action)
        {
            try
            {
                MyAPIGateway.Utilities.InvokeOnGameThread(() =>
                                                          {
                                                              try
                                                              {
                                                                  action();
                                                              }
                                                              catch
                                                              {
                                                                  //don't care
                                                              }
                                                          });
            }
            catch
            {
                //don't care
            }
        }
    }
}
