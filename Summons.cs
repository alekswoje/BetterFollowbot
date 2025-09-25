using System.Linq;
using ExileCore.PoEMemory.Components;

namespace BetterFollowbot;

public class Summons
{
    public static float GetLowestMinionHpp()
    {
        float hpp = 100;
        foreach (var obj in BetterFollowbot.Instance.localPlayer.GetComponent<Actor>().DeployedObjects
                     .Where(x => x?.Entity?.GetComponent<Life>() != null))
            if (obj.Entity.GetComponent<Life>().HPPercentage < hpp)
                hpp = obj.Entity.GetComponent<Life>().HPPercentage;
        return hpp;
    }

    public static float GetAnimatedGuardianHpp()
    {
        const float hpp = 100;
        DeployedObject animatedGuardian = null;
        animatedGuardian = BetterFollowbot.Instance.localPlayer.GetComponent<Actor>().DeployedObjects.FirstOrDefault(x =>
            x?.Entity?.GetComponent<Life>() != null && x.Entity.Path.Contains("AnimatedArmour"));
        return animatedGuardian?.Entity.GetComponent<Life>().HPPercentage ?? hpp;
    }

    public static int GetSkeletonCount()
    {
        try
        {
            return BetterFollowbot.Instance.localPlayer.GetComponent<Actor>().DeployedObjects
                .Count(x => x?.Entity != null && x.Entity.IsAlive &&
                           (x.Entity.Path.Contains("Skeleton") ||
                            x.Entity.Path.Contains("skeleton") ||
                            x.Entity.Metadata.ToLower().Contains("skeleton")));
        }
        catch
        {
            return 0;
        }
    }

    public static int GetRagingSpiritCount()
    {
        try
        {
            return BetterFollowbot.Instance.localPlayer.GetComponent<Actor>().DeployedObjects
                .Count(x => x?.Entity != null && x.Entity.IsAlive &&
                           (x.Entity.Path.Contains("RagingSpirit") ||
                            x.Entity.Path.Contains("ragingspirit") ||
                            x.Entity.Metadata.ToLower().Contains("ragingspirit") ||
                            x.Entity.Path.Contains("Raging Spirit") ||
                            x.Entity.Metadata.ToLower().Contains("raging spirit")));
        }
        catch
        {
            return 0;
        }
    }

    public static int GetTotalMinionCount()
    {
        try
        {
            return BetterFollowbot.Instance.localPlayer.GetComponent<Actor>().DeployedObjects
                .Count(x => x?.Entity != null && x.Entity.IsAlive);
        }
        catch
        {
            return 0;
        }
    }
}