// T7: agentId <-> Agent map. The player is registered as `IsHuman = true` so
// AgentDecision.Tick (T9) skips them — per INV-3.10-1.
using System.Collections.Generic;

namespace Jyx2.AITavern
{
    public class NPCRegistry
    {
        readonly Dictionary<GameId, Agent> _byId = new Dictionary<GameId, Agent>();
        readonly System.Random _rng;

        public NPCRegistry(System.Random rng = null)
        {
            _rng = rng ?? new System.Random();
        }

        public void Register(Agent agent)
        {
            if (!agent.AgentId.IsValid)
                throw new System.InvalidOperationException("Agent.AgentId must be set before Register.");
            _byId[agent.AgentId] = agent;
        }

        public void Unregister(GameId agentId)
        {
            _byId.Remove(agentId);
        }

        public Agent Get(GameId agentId)
        {
            return _byId.TryGetValue(agentId, out var a) ? a : null;
        }

        public IEnumerable<Agent> All
        {
            get { return _byId.Values; }
        }

        // INV-3.10-1: AgentDecision.Tick iterates this and so excludes humans.
        public IEnumerable<Agent> NonHumanAgents
        {
            get
            {
                foreach (var a in _byId.Values)
                    if (!a.IsHuman) yield return a;
            }
        }

        /// <summary>
        /// Convenience lookup for Conversation.Tick (T5) which takes a
        /// Func&lt;GameId, INPCBody&gt; bodyOf. Returns null if not found.
        /// </summary>
        public INPCBody GetBody(GameId agentId)
        {
            return _byId.TryGetValue(agentId, out var a) ? a.Body : null;
        }
    }
}
