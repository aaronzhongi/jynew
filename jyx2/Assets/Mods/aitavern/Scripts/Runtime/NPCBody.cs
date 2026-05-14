// T7: MonoBehaviour wrapping a NavMeshAgent. Implements INPCBody (T5) so
// pure-C# logic in AITavern.Runtime can drive position queries and pathfinding
// without depending on Unity NavMesh details. Added as a component on each
// spawned NPC GameObject by AITavernBoot (T15).
using UnityEngine;
using UnityEngine.AI;

namespace Jyx2.AITavern
{
    [RequireComponent(typeof(NavMeshAgent))]
    public class NPCBody : MonoBehaviour, INPCBody
    {
        // Wired by AITavernBoot on spawn (T15).
        public GameId AgentId;

        NavMeshAgent _nav;
        bool _hasDestination;

        void Awake()
        {
            _nav = GetComponent<NavMeshAgent>();
        }

        public Vector3 Position => transform.position;

        public bool IsPathfinding
        {
            get
            {
                if (_nav == null || !_nav.isOnNavMesh) return false;
                if (!_hasDestination) return false;
                // pathPending = path is being computed; remainingDistance > stoppingDistance
                // means we haven't arrived yet.
                return _nav.pathPending || _nav.remainingDistance > _nav.stoppingDistance + 0.01f;
            }
        }

        public void StopPathfinding()
        {
            if (_nav == null || !_nav.isOnNavMesh) return;
            _nav.ResetPath();
            _hasDestination = false;
        }

        public void MoveTo(Vector3 destination)
        {
            if (_nav == null || !_nav.isOnNavMesh) return;
            _nav.SetDestination(destination);
            _hasDestination = true;
        }
    }
}
