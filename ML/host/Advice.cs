using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using TinyTribe.Brains;
using TinyTribe.Sim.Core;
using TinyTribe.Sim.Data;

namespace TinyTribe.Host
{
    /// <summary>
    /// What the scripted bot would have advised in recorded runs, villager by villager and tick by tick
    /// (the bot asked as a trainer asks it: EnvOptions.Expert), and whether the villagers did it.
    /// For working out why learners that are corrected by the bot do or do not pick something up.
    /// It reads the bot's private notes (who it takes for the builder, where each villager is headed)
    /// by reflection, so that the bot itself stays as it is.
    /// </summary>
    static class Advice
    {
        const BindingFlags Hidden = BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Public;

        sealed class Tally
        {
            public long Asked, Done;
        }

        public static int Run(GameData data, Dictionary<string, string> options)
        {
            string runs = options.TryGetValue("runs", out string r) ? r : throw new ArgumentException("Missing --runs");
            int top = int.Parse(options.TryGetValue("top", out string t) ? t : "18");
            bool daily = options.ContainsKey("daily");
            // --steps file.csv: every step the bot advised, with where it was headed, for looking at outside this program.
            StreamWriter steps = options.TryGetValue("steps", out string stepsPath) ? new StreamWriter(stepsPath) : null;
            steps?.WriteLine("village,tick,agent,goal,target_dx,target_dy,advised,played,in_hand,role");

            var builderField = typeof(SurvivalBrain).GetField("builder", Hidden);
            var plansField = typeof(SurvivalBrain).GetField("plans", Hidden);
            var planType = typeof(SurvivalBrain).GetNestedType("Plan", BindingFlags.NonPublic);
            var goalField = planType.GetField("Goal", Hidden);
            var foragingField = planType.GetField("Foraging", Hidden);
            var wateringField = planType.GetField("Watering", Hidden);
            var targetField = planType.GetField("TargetTile", Hidden);
            // Goal -> which needle of the compass shows that kind of thing (water, food, wood, stone).
            var needleOf = new Dictionary<string, int> { ["Drink"] = 0, ["FetchWater"] = 0, ["Forage"] = 1, ["Wood"] = 2, ["Stone"] = 3 };
            var compass = new Dictionary<string, long[]>();
            var differs = new Dictionary<string, long[]>();
            byte[] grid = null;
            float[] vector = null;
            int radius = data.ConfigInt("obs_compass_radius");

            int wood = data.ItemById["pine_wood"].Int, stone = data.ItemById["cobblestone"].Int, water = data.ItemById["water"].Int;
            var hunger = new Dictionary<int, float>();
            foreach (var item in data.Items)
                if (item.Consumable != 0 && item.Hunger > 0f && string.IsNullOrEmpty(item.SicknessOnEat)) hunger[item.Int] = item.Hunger;
            var actionName = data.Actions.ToDictionary(a => a.Id, a => a.Name);
            var argName = data.ActionArgs.ToDictionary(a => a.Int, a => a.RefId);
            var roleName = data.Roles.ToDictionary(x => x.Int, x => x.Id);

            // 0: the bot's builder while it holds the builder's job, 1: while it holds another job or none, 2: everyone else.
            string[] whoName = { "the builder, holding the job", "the builder, without the job", "everyone else" };
            var tallies = new[] { new Dictionary<string, Tally>(), new Dictionary<string, Tally>(), new Dictionary<string, Tally>() };
            long builderChanges = 0, villages = 0, noBuilder = 0;
            var ticksOf = new long[3];
            double[] woodOf = new double[3], stoneOf = new double[3], foodOf = new double[3], freeOf = new double[3], slotsOf = new double[3];
            var jobs = new Dictionary<string, long>();
            long jobSamples = 0;
            int builderJob = data.RoleInt("builder");

            foreach (string folder in Directory.GetDirectories(runs).OrderBy(f => f))
            {
                if (!File.Exists(Path.Combine(folder, "run.json"))) continue;
                var run = RunReplay.Read(folder);
                var bot = new SurvivalBrain(run.Setup.Seed) { Memoryless = !options.ContainsKey("remembers"), Situational = options.ContainsKey("situational") };
                // --compare: a second bot that keeps no plan at all, asked the same; how often the two would advise differently.
                var pure = options.ContainsKey("compare") ? new SurvivalBrain(run.Setup.Seed) { Memoryless = true, Situational = true } : null;
                int lastBuilder = -2, lastDay = -1, lastWood = -1;
                long traceUntil = long.Parse(options.TryGetValue("trace", out string tr) ? tr : "0");
                var day = new Dictionary<string, Tally>();
                villages++;

                RunReplay.Replay(data, folder, out string problem, long.MaxValue, null, (sim, played) =>
                {
                    if (daily && villages == 1 && sim.Day != lastDay)
                    {
                        if (lastDay >= 0) Console.WriteLine("  day " + (lastDay + 1) + ": " + Line(day, 5));
                        day.Clear();
                        lastDay = sim.Day;
                    }
                    for (int i = 0; i < sim.Agents.Count; i++)
                    {
                        var agent = sim.Agents[i];
                        if (!agent.Alive) continue;
                        var advice = bot.Decide(sim, i);
                        if (pure != null)
                        {
                            var other = pure.Decide(sim, i);
                            string goalNow = goalField.GetValue(((IList)plansField.GetValue(bot))[i]).ToString();
                            string kind = (IsMove(sim, advice.Id) ? "step toward " : actionName[advice.Id] + " for ") + goalNow
                                + (i == (int)builderField.GetValue(bot) ? " (builder)" : "");
                            if (!differs.TryGetValue(kind, out var tally)) differs[kind] = tally = new long[3];
                            tally[0]++;
                            if (other.Id != advice.Id || other.Arg != advice.Arg) tally[1]++;
                            if (IsMove(sim, advice.Id) != IsMove(sim, other.Id)) tally[2]++;
                        }
                        int builder = (int)builderField.GetValue(bot);
                        var plan = ((IList)plansField.GetValue(bot))[i];
                        string what = actionName[advice.Id];
                        if (what.StartsWith("move")) what = "move";
                        if (advice.Arg != 0 && !what.StartsWith("move") && argName.TryGetValue(advice.Arg, out string arg)) what += " " + arg;
                        string key = what + " (" + goalField.GetValue(plan)
                            + ((bool)foragingField.GetValue(plan) ? ", foraging" : "") + ((bool)wateringField.GetValue(plan) ? ", watering" : "") + ")";
                        bool did = played[i].Id == advice.Id && played[i].Arg == advice.Arg;
                        string goalName = goalField.GetValue(plan).ToString();
                        int target = (int)targetField.GetValue(plan);
                        if (steps != null && target >= 0 && IsMove(sim, advice.Id))
                            steps.WriteLine(villages + "," + sim.Tick + "," + i + "," + goalName + "," + (target % sim.World.Width - agent.X) + ","
                                + (target / sim.World.Width - agent.Y) + "," + actionName[advice.Id] + "," + actionName[played[i].Id] + ","
                                + agent.Equipped[(int)EquipSlot.Hand].Item + "," + agent.Role);
                        if (target >= 0 && needleOf.TryGetValue(goalName, out int needle) && IsMove(sim, advice.Id))
                        {
                            grid = grid ?? new byte[sim.Observation.GridLength];
                            vector = vector ?? new float[sim.Observation.VectorLength];
                            sim.Observe(i, grid, vector);
                            int at = sim.Observation.Fields["compass"].Start + needle * 3;
                            int far = (int)Math.Round((1f - vector[at + 2]) * radius);
                            bool shown = vector[at + 2] > 0f || vector[at] != 0f || vector[at + 1] != 0f;
                            int cx = (int)Math.Round(vector[at] * far), cy = (int)Math.Round(vector[at + 1] * far);
                            int tx = target % sim.World.Width - agent.X, ty = target / sim.World.Width - agent.Y;
                            string walker = goalName.PadRight(11) + (i != builder ? "others" : agent.Role == builderJob ? "builder with job" : "builder, no job");
                            if (!compass.TryGetValue(walker, out var c)) compass[walker] = c = new long[14];
                            c[0]++;
                            if (!shown) c[1]++;
                            else if (cx == tx && cy == ty) c[2]++;
                            else c[3]++;
                            if (shown)
                            {
                                int along = Along(sim, advice.Id, cx, cy);
                                c[along > 0 ? 4 : along < 0 ? 5 : 6]++;
                                if (IsMove(sim, played[i].Id))
                                {
                                    c[7]++;
                                    if (Along(sim, played[i].Id, cx, cy) > 0) c[8]++;
                                    bool toward = Along(sim, played[i].Id, tx, ty) > 0;
                                    if (toward) c[9]++;
                                    int split = cx == tx && cy == ty ? 10 : 12;
                                    c[split]++;
                                    if (toward) c[split + 1]++;
                                }
                            }
                        }
                        int who = i != builder ? 2 : agent.Role == builderJob ? 0 : 1;
                        Count(tallies[who], key, did);
                        ticksOf[who]++;
                        woodOf[who] += agent.Count(wood);
                        stoneOf[who] += agent.Count(stone);
                        freeOf[who] += agent.FreeSlots;
                        slotsOf[who] += agent.InventoryCapacity;
                        for (int s = 0; s < agent.InventoryCapacity; s++)
                            if (agent.Inventory[s].Qty > 0 && hunger.TryGetValue(agent.Inventory[s].Item, out float points)) foodOf[who] += points * agent.Inventory[s].Qty;
                        if (sim.Tick % 240 == 120)
                        {
                            string job = roleName.TryGetValue(agent.Role, out string jn) ? jn : "?";
                            jobs[job] = (jobs.TryGetValue(job, out long had) ? had : 0) + 1;
                            jobSamples++;
                        }
                        if (i != builder) continue;

                        if (daily && villages == 1) Count(day, key, did);
                        if (villages == 1 && sim.Tick < traceUntil)
                        {
                            string playedName = actionName[played[i].Id] + (played[i].Arg != 0 ? " " + played[i].Arg : "");
                            bool notable = agent.Count(wood) != lastWood || i != lastBuilder || !(playedName.StartsWith("move") || playedName.StartsWith("sleep") || playedName.StartsWith("harvest") || playedName.StartsWith("emote"));
                            if (notable || options.ContainsKey("every"))
                                Console.WriteLine("  t" + sim.Tick + " v" + i + " " + (roleName.TryGetValue(agent.Role, out string rn) ? rn : "-") + " at " + agent.X + "," + agent.Y
                                    + " wood " + agent.Count(wood) + " free " + agent.FreeSlots + "/" + agent.InventoryCapacity + " | bot: " + key + " | did: " + playedName);
                            lastWood = agent.Count(wood);
                        }
                    }
                    int now = (int)builderField.GetValue(bot);
                    if (now < 0) noBuilder++;
                    if (now != lastBuilder && lastBuilder != -2) builderChanges++;
                    lastBuilder = now;
                });
                if (problem != null) Console.WriteLine(Path.GetFileName(folder) + ": " + problem);
            }

            steps?.Dispose();
            if (differs.Count > 0)
            {
                Console.WriteLine("the bot's advice (plans kept while obeyed) against a bot that keeps no plan: ticks, advice differs, one walks and the other does not");
                foreach (var pair in differs.OrderByDescending(x => x.Value[0]).Take(top))
                    Console.WriteLine("  " + pair.Key.PadRight(40) + pair.Value[0].ToString().PadLeft(8) + Percent(pair.Value[1], pair.Value[0]).PadLeft(9)
                        + Percent(pair.Value[2], pair.Value[0]).PadLeft(9));
            }
            Console.WriteLine(villages + " villages; the villager the bot takes for the builder held the builder's job in "
                + Percent(ticksOf[0], ticksOf[0] + ticksOf[1]) + " of its ticks, and changed " + (builderChanges / (double)Math.Max(1, villages)).ToString("0.0")
                + " times per village; ticks with no builder: " + noBuilder);
            Console.WriteLine("jobs held at midday, share of villagers: " + string.Join(", ", jobs.OrderByDescending(j => j.Value).Select(j => j.Key + " " + Percent(j.Value, jobSamples))));
            for (int w = 0; w < 3; w++)
                if (ticksOf[w] > 0)
                    Console.WriteLine(whoName[w].PadRight(30) + ticksOf[w].ToString().PadLeft(8) + " ticks; carried on average: wood " + (woodOf[w] / ticksOf[w]).ToString("0.00")
                        + ", stone " + (stoneOf[w] / ticksOf[w]).ToString("0.00") + ", food points " + (foodOf[w] / ticksOf[w]).ToString("0.0")
                        + ", free slots " + (freeOf[w] / ticksOf[w]).ToString("0.00") + " of " + (slotsOf[w] / ticksOf[w]).ToString("0.00"));
            Console.WriteLine("\nwalking to something the compass has a needle for: is the needle on what the bot is walking to?");
            Console.WriteLine("  goal         steps   no needle  same thing  another thing | the bot's step: with the needle  against it  across it | the villager's step: with the needle  toward the bot's target");
            foreach (var pair in compass.OrderBy(x => x.Key))
            {
                var c = pair.Value;
                long shown = c[2] + c[3];
                Console.WriteLine("  " + pair.Key.PadRight(28) + c[0].ToString().PadLeft(7) + Percent(c[1], c[0]).PadLeft(12) + Percent(c[2], c[0]).PadLeft(12) + Percent(c[3], c[0]).PadLeft(15)
                    + " |" + Percent(c[4], shown).PadLeft(32) + Percent(c[5], shown).PadLeft(12) + Percent(c[6], shown).PadLeft(11)
                    + " |" + Percent(c[8], c[7]).PadLeft(37) + Percent(c[9], c[7]).PadLeft(25)
                    + " (needle on it: " + Percent(c[11], c[10]) + ", needle elsewhere: " + Percent(c[13], c[12]) + ")");
            }
            for (int w = 0; w < 3; w++)
            {
                Console.WriteLine("\nwhat the bot advised " + whoName[w] + " (where it was headed): share, done as advised");
                Print(tallies[w], top);
            }
            return 0;
        }

        static bool IsMove(Simulation sim, int action) =>
            action == sim.Actions.MoveEast || action == sim.Actions.MoveWest || action == sim.Actions.MoveNorth || action == sim.Actions.MoveSouth;

        /// <summary>+1 when the step goes the way (dx, dy) points along its own axis, -1 when the other way, 0 when (dx, dy) has nothing along that axis.</summary>
        static int Along(Simulation sim, int action, int dx, int dy)
        {
            var a = sim.Actions;
            if (action == a.MoveEast) return Math.Sign(dx);
            if (action == a.MoveWest) return -Math.Sign(dx);
            if (action == a.MoveNorth) return Math.Sign(dy);
            return -Math.Sign(dy);
        }

        static void Count(Dictionary<string, Tally> tally, string key, bool did)
        {
            if (!tally.TryGetValue(key, out var entry)) tally[key] = entry = new Tally();
            entry.Asked++;
            if (did) entry.Done++;
        }

        static string Percent(long part, long whole) => (100.0 * part / Math.Max(1, whole)).ToString("0.0") + "%";

        static string Line(Dictionary<string, Tally> tally, int top)
        {
            long all = tally.Values.Sum(v => v.Asked);
            return string.Join("; ", tally.OrderByDescending(p => p.Value.Asked).Take(top)
                .Select(p => p.Key + " " + Percent(p.Value.Asked, all) + "/" + Percent(p.Value.Done, p.Value.Asked)));
        }

        static void Print(Dictionary<string, Tally> tally, int top)
        {
            long all = tally.Values.Sum(v => v.Asked);
            foreach (var pair in tally.OrderByDescending(p => p.Value.Asked).Take(top))
                Console.WriteLine("  " + pair.Key.PadRight(64) + Percent(pair.Value.Asked, all).PadLeft(7) + Percent(pair.Value.Done, pair.Value.Asked).PadLeft(12));
        }
    }
}
