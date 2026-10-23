namespace FamilyDispatch.Web.Domain;

/// <summary>The only place that decides how a load's status may change: one step forward, never skip, never back.</summary>
public static class LoadWorkflow
{
    /// <summary>The steps in order, for display.</summary>
    public static readonly LoadStatus[] Steps = [LoadStatus.Assigned, LoadStatus.PickedUp, LoadStatus.Delivered];

    /// <summary>Driver and truck can change only before pickup: once a load is on the road it stays with its driver.</summary>
    public static bool CanReassign(LoadStatus status) => status == LoadStatus.Assigned;

    public static LoadStatus? Next(LoadStatus from) => from switch
    {
        LoadStatus.Assigned => LoadStatus.PickedUp,
        LoadStatus.PickedUp => LoadStatus.Delivered,
        _ => null,
    };

    public static bool CanMove(LoadStatus from, LoadStatus to) => Next(from) == to;

    /// <summary>Applies the move and stamps its time. Throws if the move is not allowed.</summary>
    public static void Move(Load load, LoadStatus to, DateTime nowUtc)
    {
        if (!CanMove(load.Status, to))
            throw new InvalidOperationException($"Load {load.ReferenceNo} cannot move from {load.Status} to {to}.");

        load.Status = to;
        if (to == LoadStatus.PickedUp) load.PickedUpAtUtc = nowUtc;
        if (to == LoadStatus.Delivered) load.DeliveredAtUtc = nowUtc;
    }
}
