// T5: Forward-declared interface for the NPC body component owned by T7
// (NPCBody.cs / NPCRegistry.cs). Declared here so Agent.cs and Conversation.cs
// can talk to the body abstraction without taking a dependency on the
// NavMeshAgent-backed concrete class (which lives in a parallel task).
namespace Jyx2.AITavern
{
    public interface INPCBody
    {
        UnityEngine.Vector3 Position { get; }
        bool IsPathfinding { get; }
        void StopPathfinding();
        void MoveTo(UnityEngine.Vector3 destination);
    }
}
