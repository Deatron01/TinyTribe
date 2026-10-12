using System.Collections.Generic;

namespace TinyTribe.Sim.Core
{
    public enum VoteKind { None = 0, Role = 1, Research = 2, Conviction = 3 }

    /// <summary>A vote the Council is holding right now.</summary>
    public sealed class Vote
    {
        public VoteKind Kind;

        /// <summary>The villager a role vote is about, or -1.</summary>
        public int Subject = -1;

        /// <summary>A role vote held in a job review (R41): its villager may have a job already.</summary>
        public bool Review;

        public long OpenedTick;

        /// <summary>The vote is counted once the tick counter reaches this, or earlier when everyone has voted.</summary>
        public long ClosesTick;

        /// <summary>The option each villager chose, by agent index; 0 for those who have not voted.</summary>
        readonly List<int> ballots = new List<int>();

        public int BallotCount => ballots.Count;

        public bool HasVoted(int agentIndex) => BallotOf(agentIndex) != 0;

        public int BallotOf(int agentIndex) => (uint)agentIndex < (uint)ballots.Count ? ballots[agentIndex] : 0;

        internal void Cast(int agentIndex, int option)
        {
            while (ballots.Count <= agentIndex) ballots.Add(0);
            ballots[agentIndex] = option;
        }
    }

    /// <summary>The Council: votes that give villagers their roles, and go over them again as the village changes (Sim Rules R16, R17, R41).</summary>
    public sealed partial class Simulation
    {
        Vote openVote;
        readonly Queue<int> roleVotesDue = new Queue<int>();
        readonly Queue<int> reviewsDue = new Queue<int>();
        bool reviewCalled;
        long nextReviewTick;
        float[] voteTally;
        int roleCount;
        int councilSeason;

        /// <summary>The vote that is open, or null.</summary>
        public Vote OpenVote => openVote;

        /// <summary>R41: whether the Council goes over the jobs again as the village changes. Off, a job once given is kept.</summary>
        public bool JobReviewsEnabled { get; set; } = true;

        /// <summary>R41: has the Council go over everyone's job, as soon as it is free and the gap since the last review is over.</summary>
        public void CallJobReview() => reviewCalled = true;

        /// <summary>Something happened that may change who is best placed where.</summary>
        void JobsMayHaveChanged()
        {
            if (JobReviewsEnabled) reviewCalled = true;
        }

        void InitCouncil()
        {
            foreach (var role in Data.Roles) roleCount = System.Math.Max(roleCount, role.Int + 1);
            voteTally = new float[System.Math.Max(argCount, roleCount)];
            councilSeason = Season.Order;
        }

        /// <summary>R16: one vote per villager per vote, for an option that fits what is being voted on.</summary>
        bool TryVote(Agent agent, int option, bool execute)
        {
            if (agent.Child || openVote == null || openVote.HasVoted(agent.Index) || !IsVoteOption(openVote, option)) return false;
            if (!execute) return true;
            openVote.Cast(agent.Index, option);
            events.Add(new SimEvent(SimEventType.VoteCast, agent.Index, option, 0, agent.X, agent.Y));
            return true;
        }

        bool IsVoteOption(Vote vote, int option)
        {
            switch (vote.Kind)
            {
                // Any role; 0 is "no role", which is not something to vote for.
                case VoteKind.Role: return option >= 1 && option < roleCount;
                // The place of a technology in Sim Tech; the first one is known from the start.
                case VoteKind.Research: return option >= 1 && option < Data.Tech.Count && CanResearch(Data.Tech[option]);
                default: return false;
            }
        }

        /// <summary>Closes the open vote when it is due and opens the next one waiting. Runs at the end of every tick.</summary>
        void UpdateCouncil()
        {
            // R17: at the turn of the season, everyone still without a role is put to the vote again.
            int season = Season.Order;
            if (season != councilSeason)
            {
                councilSeason = season;
                JobsMayHaveChanged();
                foreach (var agent in agents)
                {
                    bool waiting = roleVotesDue.Contains(agent.Index) || (openVote != null && openVote.Subject == agent.Index);
                    if (agent.Alive && agent.Role == 0 && !agent.Child && !waiting) roleVotesDue.Enqueue(agent.Index);
                }
            }

            if (openVote != null)
            {
                // Nothing left to decide when the villager has died, or has got its first role some other way.
                var about = openVote.Subject >= 0 ? agents[openVote.Subject] : null;
                bool settled = openVote.Kind == VoteKind.Role && !openVote.Review && about != null && about.Role != 0;
                if (about != null && (!about.Alive || settled)) CloseVote(counted: false);
                else if (Tick >= openVote.ClosesTick || EveryoneHasVoted()) CloseVote(counted: true);
            }

            while (openVote == null && roleVotesDue.Count > 0)
            {
                var subject = agents[roleVotesDue.Dequeue()];
                // R22: a child is given no job; the vote comes when it is grown.
                if (!subject.Alive || subject.Role != 0 || subject.Child) continue;
                openVote = new Vote
                {
                    Kind = VoteKind.Role, Subject = subject.Index, OpenedTick = Tick, ClosesTick = Tick + Config.VoteDurationTicks,
                };
                events.Add(new SimEvent(SimEventType.VoteOpened, subject.Index, (int)VoteKind.Role, 0, subject.X, subject.Y));
            }

            // R41: a review that was called starts once the Council is free and the gap since the last one is over.
            if (reviewCalled && openVote == null && reviewsDue.Count == 0 && Tick >= nextReviewTick)
            {
                reviewCalled = false;
                nextReviewTick = Tick + Config.JobReviewGapTicks;
                foreach (var agent in agents)
                    if (agent.Alive) reviewsDue.Enqueue(agent.Index);
            }
            while (openVote == null && reviewsDue.Count > 0)
            {
                var subject = agents[reviewsDue.Dequeue()];
                if (!subject.Alive) continue;
                openVote = new Vote
                {
                    Kind = VoteKind.Role, Subject = subject.Index, Review = true, OpenedTick = Tick, ClosesTick = Tick + Config.VoteDurationTicks,
                };
                events.Add(new SimEvent(SimEventType.VoteOpened, subject.Index, (int)VoteKind.Role, 0, subject.X, subject.Y));
            }
            if (openVote == null) OpenResearchVote();
        }

        bool EveryoneHasVoted()
        {
            bool anyone = false;
            foreach (var agent in agents)
            {
                if (!agent.Alive) continue;
                if (!openVote.HasVoted(agent.Index)) return false;
                anyone = true;
            }
            return anyone;
        }

        /// <summary>R16: the option with the most weight wins; a tie or an empty vote changes nothing.</summary>
        void CloseVote(bool counted)
        {
            var vote = openVote;
            openVote = null;
            int winner = 0;
            if (counted)
            {
                System.Array.Clear(voteTally, 0, voteTally.Length);
                for (int i = 0; i < vote.BallotCount; i++)
                {
                    int option = vote.BallotOf(i);
                    if (option != 0) voteTally[option] += 1f + agents[i].Stats.Influence;
                }
                float best = 0f;
                bool tied = false;
                for (int option = 1; option < voteTally.Length; option++)
                {
                    if (voteTally[option] > best)
                    {
                        best = voteTally[option];
                        winner = option;
                        tied = false;
                    }
                    else if (voteTally[option] == best && best > 0f) tied = true;
                }
                if (tied) winner = 0;
            }

            var subject = vote.Subject >= 0 ? agents[vote.Subject] : null;
            int x = subject != null ? subject.X : World.SpawnX, y = subject != null ? subject.Y : World.SpawnY;
            events.Add(new SimEvent(SimEventType.VoteClosed, vote.Subject, (int)vote.Kind, winner, x, y));
            if (vote.Kind == VoteKind.Research) ResearchVoteClosed(winner);
            if (winner == 0) return;

            // In a review the Council may well confirm the job the villager has.
            if (vote.Kind == VoteKind.Role && subject.Role != winner)
            {
                subject.Role = winner;
                events.Add(new SimEvent(SimEventType.RoleAssigned, subject.Index, winner, 0, x, y));
            }
        }
    }
}
