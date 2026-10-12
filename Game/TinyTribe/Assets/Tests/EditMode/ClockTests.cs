using NUnit.Framework;

namespace TinyTribe.Tests
{
    public class ClockTests
    {
        static int TicksPerHour => Kit.Data.ConfigInt("ticks_per_hour");
        static int DayStart => Kit.Data.ConfigInt("day_start_hour");
        static int NightStart => Kit.Data.ConfigInt("night_start_hour");

        [Test]
        public void Simulation_starts_in_the_morning_of_day_zero()
        {
            var sim = Kit.NewSim();

            Assert.AreEqual(0, sim.Tick);
            Assert.AreEqual(DayStart, sim.Hour);
            Assert.AreEqual(0, sim.Day);
            Assert.IsFalse(sim.IsNight);
            Assert.AreEqual(Kit.Data.ConfigString("start_season"), sim.Season.Id);
        }

        [Test]
        public void Night_begins_at_the_night_start_hour()
        {
            var sim = Kit.NewSim();
            int ticksUntilNight = (NightStart - DayStart) * TicksPerHour;

            Kit.Advance(sim, ticksUntilNight - 1);
            Assert.IsFalse(sim.IsNight);

            Kit.Advance(sim, 1);
            Assert.IsTrue(sim.IsNight);
            Assert.AreEqual(NightStart, sim.Hour);
        }

        [Test]
        public void Night_ends_at_the_day_start_hour()
        {
            var sim = Kit.NewSim();

            Kit.AdvanceTo(sim, 1, DayStart - 1);
            Assert.IsTrue(sim.IsNight);

            Kit.AdvanceTo(sim, 1, DayStart);
            Assert.IsFalse(sim.IsNight);
        }

        [Test]
        public void Day_counter_increments_at_midnight()
        {
            var sim = Kit.NewSim();

            Kit.Advance(sim, (Kit.Data.ConfigInt("hours_per_day") - DayStart) * TicksPerHour);

            Assert.AreEqual(1, sim.Day);
            Assert.AreEqual(0, sim.Hour);
        }

        [Test]
        public void Season_changes_after_days_per_season()
        {
            var sim = Kit.NewSim();
            int days = Kit.Data.ConfigInt("days_per_season");

            Kit.AdvanceTo(sim, days - 1, 23);
            Assert.AreEqual("spring", sim.Season.Id);

            Kit.AdvanceTo(sim, days, 0);
            Assert.AreEqual("summer", sim.Season.Id);
        }

        [Test]
        public void Year_wraps_back_to_spring()
        {
            var sim = Kit.NewSim();

            Kit.AdvanceTo(sim, 4 * Kit.Data.ConfigInt("days_per_season"), 0);

            Assert.AreEqual("spring", sim.Season.Id);
        }
    }
}
