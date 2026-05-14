// T5: Typed-string identity wrapper for agents/players/conversations.
// Mirrors ai-town's `GameId<'agents'>` brand pattern; in C# we use a readonly struct
// so equality and hashing are value-based and Empty is a meaningful sentinel.
namespace Jyx2.AITavern
{
    public readonly struct GameId : System.IEquatable<GameId>
    {
        public readonly string Value;

        public GameId(string v) { Value = v; }

        public bool Equals(GameId o) => Value == o.Value;
        public override bool Equals(object o) => o is GameId g && Equals(g);
        public override int GetHashCode() => Value?.GetHashCode() ?? 0;
        public override string ToString() => Value;

        public static bool operator ==(GameId a, GameId b) => a.Equals(b);
        public static bool operator !=(GameId a, GameId b) => !a.Equals(b);

        public static readonly GameId Empty = new GameId(null);

        public bool IsValid => !string.IsNullOrEmpty(Value);
    }
}
