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
 *  Mod by Rexxar. Developed for Doctor Octoganapus. All planet assets belong
 *  exclusively to the creator, I claim only this script.                                     Well only partly by now.
 *  
 *  No license. Feel free to use this script however you want. All I ask is that
 *  you give credit and leave this entire comment block intact.
 *  If you're feeling especialy nice, you can donate at http://paypal.me/rexxar
 *    
 *  Updated, Commented(WIP) and "Improved" by JanikBey
 *  My Part is also free to use. I encourage you to change the script too suit your needs.
 *  I try to comment everything, so you can learn to use this script faster. And I am sorry for my spelling.
 *  My original workshop link: https://steamcommunity.com/sharedfiles/filedetails/?id=2507374909
 *  
 *  
 *  How does this script work:
 *  1.It starts with "public override void UpdateBeforeSimulation()" this activates the needed functions.
 *  2."private void ProcessDamage()" is used to find the blocks that are inside the planets atmosphere. They are then added to a list(_actionQueue).
 *  3."ProcessQueue()" is activated once per tick and will damage the Entities that are on the list and removes them from it.
 *  
 *  
 *  Features of this scipt:
 *  1.Damage customizable (amount/frequency)                                                                            feature by Rexxar
 *  2.Damage only blocks that are faced to the Sky OR damage only blocks that are on the outside of grids               feature by Rexxar
 *  3.Does not damage blocks that are in closed hangar bays (not 100% working if to close to a wall)                    feature by Rexxar
 *  4.If "ONLY_DAMAGE_FROM_ABOVE" is turned on does not damage Blocks under Voxels                                      feature by Rexxar
 *  5.Character gets damaged at semi customizable frequency                                                             feature by Rexxar/JanikBey 
 *  6.Specified blocks can have "resistance" against damage from this script                                            feature by JanikBey
 *  7.Damage can be modified for diffrent weather                                                                       feature by JanikBey
 *  8.Weather can "pierce" block damage resistance(feature 6) entirely ore partly                                       feature by JanikBey
 *  
 *  Perfomance Features:
 *  1.Script will slow down if there are too much blocks/Items that need to be damaged per frame.                       feature by JanikBey    inspired by Rexxar
 *    "Too much" can be customized
 *  2.Much of this Script runs in a background Threat                                                                   feature by Rexxar/JanikBey
 *  
 *  How does the "damage resistance" work:
 *  Damage resistance is actually more a evasion System. If a block has 50 resistance there is a 50% chance that the block doesn't take damage when it is 
 *  supposed to take damage. For 100 resistance the chance is 100%. But if the weather has a resistance penetration the resistance is reduced by that amount.
 *  For Example:    "Ceramic Armor Slope" has 100 resistance. "AcidRain" has 0 armor penetration.       Result:"Ceramic Armor Slope" will never take damage during a "AcidRain".
 *  Example 2:      "Ceramic Armor Slope" has 100 resistance. "AcidRainHeavy" has 50 armor penetration. Result:"Ceramic Armor Slope" will take halve the damage during a "AcidRainHeavy".
 *  
 *  Expected Problems:
 *  If specified blocks have localization, you need too specify them for every langauge. But this is not recommended because of potential lag.
 *  Better solution is to run the game in the language you used to specify the blocks.
 *  
 */


//don't touch "namespace"          //change "WR_Hazard_lite_rad" to something unique Like WR_Hazard_YOURPLANETNAME
namespace WR_Environmental_Hazard
{
    [MySessionComponentDescriptor(MyUpdateOrder.BeforeSimulation)]


    //change "DamageCoreHazardLiteRad" to something unique Like DamageCoreHazardYOURPLANETNAME
    public class DamageCoreHazard : MySessionComponentBase
    {
        /////////////////////CHANGE THESE FOR EACH PLANET////////////////////////////
                                                                                        //X stands for your entry 
        private const string PLANET_NAME = "Dead Planet HD";                                      //this mod targets planet X                                                 currently Urna
        private const int UPDATE_RATE = 180;                                            //damage will apply every X frames (1second =60frames)                      currently 180
        private const float LARGE_SHIP_DAMAGE = 10.0f;                                  //applies X damage to each large grid block per update                      currently 10
        private const float SMALL_SHIP_DAMAGE = 2.0f;                                   //applies X damage to each small grid block and floating items per update   currently 2
        private const string DAMAGE_STRING = PLANET_NAME + "Atmosphere";                //Damage source                                                             I recommend not touching it
        private const float PLAYER_DAMAGE_AMOUNT = 0.1f;                                //applies X damage to each player per second                                currently 0.1
        private const int WR_PLAYER_DAMAGE_RATE = 60;                                   //frequency of Player damage, !must be lower or equal to UPDATE_RATE!       currently 60
                                                                                        //frequency can only be steadily if UPDATE_RATE can be diveded through it. Otherwise you may get damaged a bit more often.
                                                                                        //if you want to have more freedoms with this I reccomend creating a second "private int _updateCount" for the character damage
        private const int MAX_ACTIONSPERTICK = 10;                                      //max Amount of Blocks/Items that can get damaged per frame. If limit is reached Script will damage blocks less frequently.     currently 10


        private const bool WEATHER_DAMAGE_ONLY=true;                                    //If true Damage only occures during specified weather
        private const bool ONLY_DAMAGE_FROM_ABOVE = true;                               //if true only "Roof" gets damaged
        private const bool DO_BLOCK_DAMAGE = true;                                      //if false blocks will not be damaged


        //write blocks that are used more often higher in the list for a little performace boost
        //same for weather (more frequent weather to the top)
        private static readonly List<WeatherResiBlock> RESISTANCE_BLOCKS = new List<WeatherResiBlock>
        {                       //Block name,             chance to ignore damage in % (only integer allowed)
            //new WeatherResiBlock("Half Light Armor Block",100),
            new WeatherResiBlock("Ceramic Armor Slope",100),
            new WeatherResiBlock("Ceramic Armor Side",100),
            new WeatherResiBlock("Ceramic Armor Face",100),
            new WeatherResiBlock("Ceramic Armor Face Inv.",100),
            new WeatherResiBlock("Ceramic Armor Flat",100),

            new WeatherResiBlock("Window Armored Side Inv.",50),
            new WeatherResiBlock("Window Armored Inv.",50),
            new WeatherResiBlock("Window Armored Side",50),
            new WeatherResiBlock("Window Armored Face",50),
            new WeatherResiBlock("Window Armored Slope",50),
            new WeatherResiBlock("Window Armored Face Inv.",50),
            new WeatherResiBlock("Window Armored Flat",50),

        };

        private static readonly List<WeatherDamageType> BAD_WEATHER_TYPES = new List<WeatherDamageType>
        {                        //Weather name,                    ship damage multiplier,   character damage multiplier,  % Resistance Piercing (only integer allowed)                     
            new WeatherDamageType("AcidRain",                       1f,                        1f,                          0            ),
            new WeatherDamageType("AcidRainHeavy",                  10f,                      10f,                          50           ),
            //new WeatherDamageType("Clear",                          1f,                       1f,                           0            ),                               //example for no weather that still deals damage without disabling weather damage entirely
        };


        /////////////////////////////////////////////////////////////////////////////

        private readonly Random _random = new Random();                                                                                         //needed for creating a random Vector
        private bool _init;                                                                                                                     //only initialize once
        private HashSet<MyPlanet> _planets = new HashSet<MyPlanet>();                                                                           //planets list
        private int _updateCount = 10000000;                                                                                                    //Counter
        private MyStringHash _damageHash;                                                                                                       //Damage Source
        private Queue<KeyValuePair<IMyDestroyableObject, float>> _actionQueue = new Queue<KeyValuePair<IMyDestroyableObject, float>>();         //List of entities that are needed to be damaged and 
        private Dictionary<IMyDestroyableObject, float> _damageEntities = new Dictionary<IMyDestroyableObject, float>();                        //List that contains entities that might be set on the "_actionQueue" list
        private int _actionsPerTick = 0;                                                                                                        //amount of objets that need to be damaged per frame
        private int _updateRate = UPDATE_RATE;                                                                                                  //damage frequency
        private bool _debug;                                                                                                                    //something about debug


        //This function is activated once before every frame
        //Coordinates the Funktions that need to be started
        public override void UpdateBeforeSimulation()
        {
            try
            {
                if (MyAPIGateway.Session == null)
                    return;

                if (!(MyAPIGateway.Session.OnlineMode == MyOnlineModeEnum.OFFLINE || MyAPIGateway.Multiplayer.IsServer))    //server only please
                    return;

                if (_updateCount % WR_PLAYER_DAMAGE_RATE == 0)                                                                 //every second
                    ProcessCharacterDamage();                                                                               //Damage Player

                ProcessQueue();                                                                                             //damage some blocks that were put inside the _actionQueue and deletes them from the list

                if (_updateCount++ < _updateRate)                                                                           //checks if its time to update the list of stuff thats needs to be damaged   also increases counter by one
                    return;

                _updateCount = 1;                                                                                           //resets counter 

                if (_debug)                                                                                                 //for debug
                    DrawDebug();

                if (!_init)                                                                                                 //initialize Planet List and damage source
                    Initialize();

                //MyAPIGateway.Utilities.ShowMessage("Update Rate", _updateRate.ToString());                                //for testing 

                lines.Clear();

                if (DO_BLOCK_DAMAGE)                                                                                        //if block damage (for this script) is on
                    MyAPIGateway.Parallel.Start(ProcessDamage);                                                             //create List of blocks that are needed to get damaged. Task uses background Threat
                }
            catch (Exception ex)
            {
                //MyAPIGateway.Utilities.ShowMessage("", ex.ToString());                                                    //if something went wrong show in ingame chat
                MyLog.Default.WriteLineAndConsole(ex.ToString());                                                           //if something went wrong write it to the log
            }
        }

        private void ProcessDamage()                                                                                        //somewhat missleading name. This Function creates The list of Blocks/ Items that will be damaged in the near future.
        {                                                                                                                   //It also contains the amount of damage they will recive.

            //Stopwatch stopwatch = new Stopwatch();                                                                        //used to calculate teh duration of this function activate the two lines at the end too to use
            //stopwatch.Start();
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

                            if (ONLY_DAMAGE_FROM_ABOVE && IsEntityCovered(entity, sphere.Center))                                                                   //Rain damage + under Roof -> no damage
                                continue;

                            if (!ONLY_DAMAGE_FROM_ABOVE && IsEntityInsideGrid(entity))                                                                              //Damage from all sides + inside -> no damage
                                continue;

                            if (WEATHER_DAMAGE_ONLY)                                                                                                                //checks if grid is inside a "Bad Weather"
                            {
                                var weatherbool = true;                                                                                                             //if true continues next: foreach (var entity in topEntities)
                                foreach (WeatherDamageType weather in BAD_WEATHER_TYPES)                                                                            
                                {
                                    if ((MyAPIGateway.Session.WeatherEffects.GetWeather(grid.WorldVolume.Center) == weather.WeatherName))
                                    {
                                        weatherbool = false;
                                        break;
                                    }
                                }
                                if (weatherbool)
                                    continue;
                            }

                            var blocks = new List<IMySlimBlock>();
                            grid.GetBlocks(blocks);

                            Vector3D offset = Vector3D.Zero;
                            if (ONLY_DAMAGE_FROM_ABOVE)
                            {
                                Vector3D direction = grid.WorldVolume.Center - sphere.Center;
                                direction.Normalize();
                                offset = direction;
                            }

                            float damage = grid.GridSizeEnum == MyCubeSize.Small ? SMALL_SHIP_DAMAGE : LARGE_SHIP_DAMAGE;
                            for (int i = 0; i < Math.Max(1, blocks.Count * 0.3); i++)
                            {
                                IMySlimBlock block;
                                if (ONLY_DAMAGE_FROM_ABOVE)
                                    block = GetRandomSkyFacingBlock(grid, blocks, offset, true);
                                else
                                    block = GetRandomExteriorBlock(grid, blocks);

                                if (block == null)                                                                                          //sometimes functions give null back, then we need to retry
                                    continue;

                                //MyAPIGateway.Utilities.ShowMessage("Block Name", block.BlockDefinition.DisplayNameText);                  //to lockup your block name !!only place a few blocks on the planet!!

                                if (WEATHER_DAMAGE_ONLY)
                                {
                                    var blockedbool = false;
                                    foreach (WeatherDamageType weather in BAD_WEATHER_TYPES)
                                    {                                       
                                        if ((MyAPIGateway.Session.WeatherEffects.GetWeather(grid.WorldVolume.Center) == weather.WeatherName))
                                        {                                          
                                            foreach (WeatherResiBlock resistantBlock in RESISTANCE_BLOCKS)
                                            {
                                                if (block.BlockDefinition.DisplayNameText == resistantBlock.BlockNameResistance)
                                                {
                                                    if ((weather.IgnoreResistancePercent + _random.Next(1, 100)) < resistantBlock.DamageResistanceChance)
                                                    {
                                                        blockedbool = true;
                                                        break;
                                                    }
                                                }
                                            }
                                            if (blockedbool)
                                                break;

                                            _damageEntities.AddOrUpdate(block, damage * weather.DamageMultiplier);
                                        }
                                    }
                                }
                                else
                                {
                                    _damageEntities.AddOrUpdate(block, damage);
                                }
                            }
                            continue;
                        }

                        var floating = entity as IMyFloatingObject;                                             //for components/tools/ores... that are floating arround
                        if (floating != null)
                        {
                            if (floating.Closed || floating.MarkedForClose)
                                continue;

                            if (IsEntityInsideGrid(grid))
                                continue;


                            if (WEATHER_DAMAGE_ONLY)
                            {
                                foreach (WeatherDamageType weather_floating in BAD_WEATHER_TYPES)
                                {
                                    if ((MyAPIGateway.Session.WeatherEffects.GetWeather(grid.WorldVolume.Center) == weather_floating.WeatherName))
                                    {
                                        _damageEntities.AddOrUpdate(floating, SMALL_SHIP_DAMAGE * weather_floating.DamageMultiplier);
                                        continue;
                                    }
                                }
                            }
                            else
                            {
                                _damageEntities.AddOrUpdate(floating, SMALL_SHIP_DAMAGE);
                            }
                            continue;
                        }
                    }
                }

                foreach (var entry in _damageEntities)
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
                if (_actionsPerTick > MAX_ACTIONSPERTICK)                                                   //slow down script if max number of objects that are allowed to be damaged per tick is reached
                {
                    _actionsPerTick = MAX_ACTIONSPERTICK;
                    _updateRate = _actionQueue.Count / MAX_ACTIONSPERTICK;
                }
                else
                {
                    _updateRate = UPDATE_RATE;                                                              //change scriptspeed to intended speed when max number of objects that are allowed to be damaged per tick is not reached
                }
                //MyAPIGateway.Utilities.ShowMessage("Update Rate", _updateRate.ToString());                //for testing
                //MyAPIGateway.Utilities.ShowMessage("Actions per Tick", _actionsPerTick.ToString());       //
                //stopwatch.Stop();                                                                         //
                //MyAPIGateway.Utilities.ShowMessage(stopwatch.ElapsedMilliseconds.ToString(), "Finish");   //
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
            foreach (var line in sphereLines)
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
                        if (ONLY_DAMAGE_FROM_ABOVE && IsEntityCovered(character, sphere.Center))                                                    //Rain damage + under Roof -> no damage
                            continue;

                        if (!ONLY_DAMAGE_FROM_ABOVE && IsEntityInsideGrid(character))                                                               //Damage from all sides + inside -> no damage
                            continue;

                        if (WEATHER_DAMAGE_ONLY)
                        {
                            foreach (WeatherDamageType weather in BAD_WEATHER_TYPES)
                            {
                                if ((MyAPIGateway.Session.WeatherEffects.GetWeather(character.WorldVolume.Center) == weather.WeatherName))
                                {
                                    character.DoDamage(PLAYER_DAMAGE_AMOUNT*weather.CharacterMultiplier, _damageHash, true);
                                    break;
                                }
                            }
                        }
                        else
                        {
                            character.DoDamage(PLAYER_DAMAGE_AMOUNT, _damageHash, true);
                        }
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
                sphereLines.Add(new LineD() { From = pos, To = RandomPositionFromPoint(ref pos, 10) });
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
                {
                    _planets.Add(planet);
                }
            }
        }

        private void Utilities_MessageEntered(string messageText, ref bool sendToOthers)
        {
            if (messageText == "debug")
                _debug = !_debug;

            if (messageText == "debugsphere")
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
        private IMySlimBlock GetRandomSkyFacingBlock(IMyCubeGrid grid, List<IMySlimBlock> blocks, Vector3D gravityDirection, bool physicsCast = false)               //gives back a "roof" block
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

        private bool IsEntityCovered(IMyEntity entity, Vector3D planetPos)        //needed for damage from above
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

            for (var i = 0; i < _actionsPerTick; i++)
            {
                KeyValuePair<IMyDestroyableObject, float> pair;
                if (!_actionQueue.TryDequeue(out pair))
                    return;
                try
                {
                    pair.Key.DoDamage(pair.Value, _damageHash, true);
                }
                catch
                {
                    //don't care
                }
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
            foreach (var item in col)
                result.Enqueue(item);

            return result;
        }

        public static void AddOrUpdate<T>(this Dictionary<T, float> dic, T key, float value)
        {
            if (dic.ContainsKey(key))
                dic[key] += value;
            else
                dic[key] = value;
        }

        public static void AddOrUpdate<T>(this Dictionary<T, int> dic, T key, int value)
        {
            if (dic.ContainsKey(key))
                dic[key] += value;
            else
                dic[key] = value;
        }
    }
    public class WeatherDamageType
    {
        public float CharacterMultiplier;
        public float DamageMultiplier;
        public string WeatherName;
        public short IgnoreResistancePercent;

        public WeatherDamageType(string weather,  float damageMult, float characterMult, short percResiPen)
        {
            WeatherName = weather;
            DamageMultiplier = damageMult;
            CharacterMultiplier = characterMult;
            IgnoreResistancePercent = percResiPen;
        }
    }
    public class WeatherResiBlock
    {
        public short DamageResistanceChance;
        public string BlockNameResistance;

        public WeatherResiBlock(string blockName, short chanceToIgnore)
        {
            BlockNameResistance = blockName;
            DamageResistanceChance = chanceToIgnore;
        }
    }
}