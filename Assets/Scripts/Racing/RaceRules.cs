// PRD-001
namespace Ybsw.Racing
{
    public static class RaceRules
    {
        public const int MinimumPlayers = 1;
        public const int MaximumPlayers = 4;
        public const ushort DefaultPort = 7777;
        public const double CountdownDurationInSeconds = 5d;

        public static bool CanStart(int playerCount)
        {
            return playerCount >= MinimumPlayers;
        }

        public static bool CanApproveConnection(int currentAndQueuedPlayerCount)
        {
            return currentAndQueuedPlayerCount < MaximumPlayers;
        }

        public static int GetNextPlace(int finishedPlayerCount)
        {
            return finishedPlayerCount + 1;
        }
    }
}
