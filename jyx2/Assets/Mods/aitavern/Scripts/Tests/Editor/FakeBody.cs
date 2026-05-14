// T17: Test double for INPCBody — Layer A only.
// Substitutes for the NavMeshAgent-backed NPCBody (T7) which cannot be
// exercised in edit-mode tests because NavMesh state isn't available
// without a scene load. Records MoveTo / StopPathfinding for assertions.
using UnityEngine;

namespace Jyx2.AITavern.Tests
{
    public class FakeBody : INPCBody
    {
        public Vector3 Position { get; set; }
        public bool IsPathfinding { get; set; }

        public int MoveToCallCount;
        public Vector3 LastMoveDestination;
        public int StopCallCount;

        public void StopPathfinding()
        {
            StopCallCount++;
            IsPathfinding = false;
        }

        public void MoveTo(Vector3 destination)
        {
            MoveToCallCount++;
            LastMoveDestination = destination;
            IsPathfinding = true;
        }
    }
}
