using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Sandbox.Game.Entities;
using Sandbox.Game.Entities.Character.Components;
using Sandbox.ModAPI;
using VRage.Game;
using VRage.Game.Components;
using VRage.Game.ModAPI;
using VRage.Game.ModAPI.Interfaces;
using VRage.ModAPI;
using VRage.Utils;
using VRageMath;

/*
 *  Mod by Rexxar. Feel free to steal, hack, reuse, send money, etc.
 */

namespace AtmosphericDamage
{
    [MySessionComponentDescriptor(MyUpdateOrder.BeforeSimulation)]
    public class DamageCoreKeris : MySessionComponentBase
    {
        /////////////////////CHANGE THESE FOR EACH PLANET////////////////////////////
        
        private const string PLANET_NAME = "DeadSun";
        private const int UPDATE_RATE = 1000; //damage will apply every 200 frames
        private const float LARGE_SHIP_DAMAGE = 25f; //applies 2.5 damage to each block per update
        private const float SMALL_SHIP_DAMAGE = 5f;
        private const string DAMAGE_STRING = PLANET_NAME + "Atmosphere";
        private bool DAMAGE_PLAYERS = true; //set true to apply atmospheric damage to players
        private const float PLAYER_DAMAGE_AMOUNT = 1f;
        private bool ACID_RAIN = false; //set true to apply damage only from above (like acid rain)
        private bool IGNORE_ATMOSPHERE = false; //set true to ignore pressurized rooms (passive radiation damage)

        /////////////////////////////////////////////////////////////////////////////

        private readonly Random _random = new Random();
        private bool _init;
        private HashSet<MyPlanet> _planets = new HashSet<MyPlanet>();
        private int _updateCount = 0;
        private MyStringHash _damageHash;
        private bool _processing;
        private Queue<KeyValuePair<IMyDestroyableObject, float>> _actionQueue = new Queue<KeyValuePair<IMyDestroyableObject, float>>();
        private Dictionary<IMyDestroyableObject, float> _damageEntities = new Dictionary<IMyDestroyableObject, float>();
        private int _actionsPerTick = 0;
        
        private bool _debug;
        
        public override void UpdateBeforeSimulation()
        {
            try
            {
                if (MyAPIGateway.Session == null)
                    return;

                //server only please
                if (!(MyAPIGateway.Session.OnlineMode == MyOnlineModeEnum.OFFLINE || MyAPIGateway.Multiplayer.IsServer))
                    return;

                if(_debug)
                    DrawDebug();
                
                if (!_init)
                    Initialize();

                if (!_processing) //worker thread is busy and writing to the queue
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

                if (DAMAGE_PLAYERS && _updateCount % 60 == 0)
                    ProcessCharacterDamage();

                if (_updateCount % UPDATE_RATE != 0)
                    return;

                if (_processing) //worker thread is busy
                    return;

                _processing = true;
                lines.Clear();
                MyAPIGateway.Parallel.Start(ProcessDamage);
            }
            catch(Exception ex)
            {
                //MyAPIGateway.Utilities.ShowMessage("", ex.ToString());
                MyLog.Default.WriteLineAndConsole(ex.ToString());
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

                            if (!IGNORE_ATMOSPHERE && !ACID_RAIN && IsEntityInsideGrid(grid))
                            {
                                //MyAPIGateway.Utilities.ShowMessage("GRID INSIDE", grid.DisplayName);
                                continue;
                            }

                            //if (ACID_RAIN && IsEntityCovered(grid, sphere.Center))
                            //{
                            //    //MyAPIGateway.Utilities.ShowMessage("GRID COVERED", grid.DisplayName);
                            //    continue;
                            //}

                            var blocks = new List<IMySlimBlock>();
                            grid.GetBlocks(blocks);

                            Vector3D offset = Vector3D.Zero;
                            if (ACID_RAIN)
                            {
                                Vector3D direction = grid.WorldVolume.Center - sphere.Center ;
                                direction.Normalize();
                                offset = direction;
                            }

                            float damage = grid.GridSizeEnum == MyCubeSize.Small ? SMALL_SHIP_DAMAGE : LARGE_SHIP_DAMAGE;
                            for (int i = 0; i < Math.Max(1, blocks.Count * 0.3); i++)
                            {
                                IMySlimBlock block;
                                if (ACID_RAIN)
                                    block = GetRandomSkyFacingBlock(grid, blocks, offset, true);
                                else
                                    block = GetRandomExteriorBlock(grid, blocks);

                                if (block == null)
                                    continue;

                                if(!_damageEntities.ContainsKey(block))
                                    _damageEntities.Add(block, 0);

                                _damageEntities[block] += damage;

                                //blocks.Remove(block);
                                //QueueInvoke(() =>
                                //            {
                                //                if (block != null && !block.Closed())
                                //                    block.DoDamage(damage, _damageHash, true);
                                //            });
                            }

                            continue;
                        }

                        var floating = entity as IMyFloatingObject;
                        if (floating != null)
                        {
                            if (floating.Closed || floating.MarkedForClose)
                                continue;

                            if (!IGNORE_ATMOSPHERE && IsEntityInsideGrid(grid))
                                continue;

                            if (ACID_RAIN && IsEntityCovered(grid, sphere.Center))
                                continue;

                            //QueueInvoke(() => floating.DoDamage(SMALL_SHIP_DAMAGE, _damageHash, true));
                            if(!_damageEntities.ContainsKey(floating))
                                _damageEntities.Add(floating, 0);

                            _damageEntities[floating] += SMALL_SHIP_DAMAGE;
                            continue;
                        }
                    }
                }

                foreach(var entry in _damageEntities)
                    _actionQueue.Enqueue(entry);

                _damageEntities.Clear();
            }
            catch (Exception ex)
            {
                //MyAPIGateway.Utilities.ShowMessage("", ex.ToString());
                MyLog.Default.WriteLineAndConsole(ex.ToString());
            }
            finally
            {
                _actionsPerTick = _actionQueue.Count / UPDATE_RATE + 1;
                _processing = false;
            }
        }

        private Vector4 color = Color.OrangeRed.ToVector4();
        private List<LineD> lines = new List<LineD>();
        private void DrawDebug()
        {
            for (int i = 0; i < lines.Count; i++)
            {
                var line = lines[i];
                MySimpleObjectDraw.DrawLine(line.From, line.To, MyStringId.GetOrCompute("WeaponLaser"), ref color, 0.1f);
            }
            foreach(var line in sphereLines)
                MySimpleObjectDraw.DrawLine(line.From, line.To, MyStringId.GetOrCompute("WeaponLaser"), ref color, 0.1f);
        }

        private void ProcessCharacterDamage()
        {
            foreach (var planet in _planets)
            {
                var sphere = new BoundingSphereD(planet.PositionComp.GetPosition(), planet.AverageRadius + planet.AtmosphereAltitude);

                var topEntities = MyAPIGateway.Entities.GetTopMostEntitiesInSphere(ref sphere);
                foreach (var entity in topEntities)
                {
                    var character = entity as IMyCharacter;
                    if (character != null)
                    {
                        if (character.Closed || character.MarkedForClose)
                            continue;

                        if (ACID_RAIN && IsEntityCovered(entity, sphere.Center))
                        {
                            //MyAPIGateway.Utilities.ShowMessage("CHARACTER COVERED", character.DisplayName);
                            continue;
                        }

                        if (!IGNORE_ATMOSPHERE)
                        {
                            var comp = character.Components.Get<MyCharacterOxygenComponent>();

                            if (comp?.OxygenSourceGrid != null && comp.OxygenLevelAtCharacterLocation > 0)
                            {
                                //MyAPIGateway.Utilities.ShowMessage("CHARACTER INSIDE", character.DisplayName);
                                continue;
                            }
                        }

                        character.DoDamage(PLAYER_DAMAGE_AMOUNT, _damageHash, true);
                    }
                }
            }
        }

        private List<LineD> sphereLines = new List<LineD>();

        private void TestSphere()
        {
            sphereLines.Clear();
            for (int i = 0; i < 300; i++)
            {
                var pos = MyAPIGateway.Session.Player.GetPosition();
                sphereLines.Add(new LineD() {From = pos, To = RandomPositionFromPoint(ref pos, 10)});
            }
        }

        private void Initialize()
        {
            _init = true;

            MyAPIGateway.Utilities.MessageEntered += Utilities_MessageEntered;

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

        private void Utilities_MessageEntered(string messageText, ref bool sendToOthers)
        {
            if (messageText == "debug")
                _debug = !_debug;
            
            if(messageText=="debugsphere")
                TestSphere();
        }
        
        /// <summary>
        /// Gets a block on the exterior hull of a ship
        /// </summary>
        /// <param name="grid"></param>
        /// <param name="blocks"></param>
        /// <returns></returns>
        private IMySlimBlock GetRandomExteriorBlock(IMyCubeGrid grid, List<IMySlimBlock> blocks)
        {
            Vector3D posInt = grid.GridIntegerToWorld(blocks.GetRandomItemFromList().Position);
            Vector3D posExt = RandomPositionFromPoint(ref posInt, grid.WorldAABB.HalfExtents.Length());

            if (_debug)
                lines.Add(new LineD() { From = posExt, To = posInt });

            Vector3I? blockPos = grid.RayCastBlocks(posExt, posInt);
            return blockPos.HasValue ? grid.GetCubeBlock(blockPos.Value) : null;
        }

        /// <summary>
        /// Finds a random block facing the sky. Raycast is slightly randomized to simulate wind blowing rain
        /// </summary>
        /// <param name="grid"></param>
        /// <param name="blocks"></param>
        /// <param name="gravityDirection"></param>
        /// <param name="physicsCast"></param>
        /// <returns></returns>
        private IMySlimBlock GetRandomSkyFacingBlock(IMyCubeGrid grid, List<IMySlimBlock> blocks, Vector3D gravityDirection, bool physicsCast = false)
        {
            Vector3D posInt = grid.GridIntegerToWorld(blocks.GetRandomItemFromList().Position) + -gravityDirection * 2;
            Vector3D posExt = posInt + gravityDirection * Math.Max(25, grid.WorldVolume.Radius * 2);
            posExt = RandomPositionFromPoint(ref posExt, grid.WorldVolume.Radius);

            Vector3I? blockPos = grid.RayCastBlocks(posExt, posInt);
            if (!blockPos.HasValue)
                return null;

            IMySlimBlock block = grid.GetCubeBlock(blockPos.Value);

            if (block == null)
                return null;

            if (physicsCast)
            {
                List<IHitInfo> hits = new List<IHitInfo>();
                MyAPIGateway.Physics.CastRay(posExt, grid.GridIntegerToWorld(blockPos.Value), hits);
                foreach (var hit in hits)
                {
                    if (hit?.HitEntity?.GetTopMostParent() == null)
                        continue;

                    if (hit.HitEntity.GetTopMostParent().EntityId != grid.EntityId)
                        return null;
                }
            }

            if (_debug)
                lines.Add(new LineD() { From = posExt, To = grid.GridIntegerToWorld(blockPos.Value) });

            return block;
        }

        /// <summary>
        /// Randomizes a vector by the given amount
        /// </summary>
        /// <param name="start"></param>
        /// <param name="distance"></param>
        /// <returns></returns>
        private Vector3D RandomPositionFromPoint(ref Vector3D start, double distance)
        {
            double z = _random.NextDouble() * 2 - 1;
            double piVal = _random.NextDouble() * 2 * Math.PI;
            double zSqrt = Math.Sqrt(1 - z * z);
            var direction = new Vector3D(zSqrt * Math.Cos(piVal), zSqrt * Math.Sin(piVal), z);

            //var mat = MatrixD.CreateFromYawPitchRoll(RandomRadian, RandomRadian, RandomRadian);
            //Vector3D direction = Vector3D.Transform(start, mat);
            direction.Normalize();
            start += direction * -2;
            return start + direction * distance;
        }

        private double RandomRadian
        {
            get { return _random.NextDouble() * MathHelper.TwoPi; }
        }

        private bool IsEntityCovered(IMyEntity entity, Vector3D planetPos)
        {
            if (entity?.Physics == null)
                return false;

            Vector3D direction = entity.WorldVolume.Center - planetPos;
            direction.Normalize();

            var hits = new List<IHitInfo>();
            MyAPIGateway.Physics.CastRay(entity.WorldVolume.Center, entity.WorldVolume.Center + direction * 50, hits);

            foreach (var hit in hits)
            {
                if (hit?.HitEntity == null)
                    continue;

                if (hit.HitEntity.EntityId == entity.EntityId)
                    continue;

                return true;
            }

            return false;
        }

        private bool IsEntityInsideGrid(IMyEntity grid)
        {
            if (grid?.Physics == null)
                return false;

            var sphere = grid.WorldVolume;
            sphere.Radius *= 10;
            var entities = MyAPIGateway.Entities.GetTopMostEntitiesInSphere(ref sphere);
            
            foreach (var entity in entities)
            {
                if (entity == null)
                    continue;

                if (entity.EntityId == grid.EntityId)
                    continue;

                var parentGrid = entity as IMyCubeGrid;
                if (parentGrid == null)
                    continue;
                
                int hitCount = 0;
                if (parentGrid.RayCastBlocks(sphere.Center, sphere.Center + grid.WorldMatrix.Forward * sphere.Radius * 10) != null)
                    hitCount++;

                if (parentGrid.RayCastBlocks(sphere.Center, sphere.Center + -grid.WorldMatrix.Forward * sphere.Radius * 10) != null)
                    hitCount++;

                if (parentGrid.RayCastBlocks(sphere.Center, sphere.Center + grid.WorldMatrix.Left * sphere.Radius * 10) != null)
                    hitCount++;

                if (parentGrid.RayCastBlocks(sphere.Center, sphere.Center + -grid.WorldMatrix.Left * sphere.Radius * 10) != null)
                    hitCount++;

                if (parentGrid.RayCastBlocks(sphere.Center, sphere.Center + grid.WorldMatrix.Up * sphere.Radius * 10) != null)
                    hitCount++;

                if (parentGrid.RayCastBlocks(sphere.Center, sphere.Center + -grid.WorldMatrix.Up * sphere.Radius * 10) != null)
                    hitCount++;

                if (hitCount > 3)
                    return true;
            }

            return false;
        }

        //spread our invoke queue over many updates to avoid lag spikes
        private void ProcessQueue()
        {
            if (_actionQueue.Count == 0)
                return;

            for (int i = 0; i < _actionsPerTick; i++)
            {
                KeyValuePair<IMyDestroyableObject, float> pair;
                if (!_actionQueue.TryDequeue(out pair))
                    return;

                pair.Key.DoDamage(pair.Value, _damageHash, true);
            }
        }

        //private void QueueInvoke(Action action)
        //{
        //    _actionQueue.Enqueue(action);
        //}

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

    public static class Extensions
    {
        public static bool Closed(this IMySlimBlock block)
        {
            return block.CubeGrid.GetCubeBlock(block.Position) == null;
        }

        public static Vector3D GetPosition(this IMySlimBlock block)
        {
            return block.CubeGrid.GridIntegerToWorld(block.Position);
        }
        
        public static Queue<T> ToQueue<T>(this IEnumerable<T> col)
        {
            var result = new Queue<T>();
            foreach(var item in col)
                result.Enqueue(item);

            return result;
        }
    }
}
