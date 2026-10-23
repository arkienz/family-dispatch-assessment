using FamilyDispatch.Web.Domain;

namespace FamilyDispatch.Tests;

public class LoadWorkflowTests
{
    // All 9 pairs: only the two single forward steps are allowed.
    [Theory]
    [InlineData(LoadStatus.Assigned, LoadStatus.Assigned, false)]
    [InlineData(LoadStatus.Assigned, LoadStatus.PickedUp, true)]
    [InlineData(LoadStatus.Assigned, LoadStatus.Delivered, false)]
    [InlineData(LoadStatus.PickedUp, LoadStatus.Assigned, false)]
    [InlineData(LoadStatus.PickedUp, LoadStatus.PickedUp, false)]
    [InlineData(LoadStatus.PickedUp, LoadStatus.Delivered, true)]
    [InlineData(LoadStatus.Delivered, LoadStatus.Assigned, false)]
    [InlineData(LoadStatus.Delivered, LoadStatus.PickedUp, false)]
    [InlineData(LoadStatus.Delivered, LoadStatus.Delivered, false)]
    public void CanMove_OnlyForwardSingleSteps(LoadStatus from, LoadStatus to, bool allowed) =>
        Assert.Equal(allowed, LoadWorkflow.CanMove(from, to));

    [Theory]
    [InlineData(LoadStatus.Assigned, true)]
    [InlineData(LoadStatus.PickedUp, false)]
    [InlineData(LoadStatus.Delivered, false)]
    public void CanReassign_OnlyBeforePickup(LoadStatus status, bool allowed) =>
        Assert.Equal(allowed, LoadWorkflow.CanReassign(status));

    [Fact]
    public void CanMove_UndefinedEnumValue_NotAllowed() =>
        Assert.False(LoadWorkflow.CanMove(LoadStatus.Assigned, (LoadStatus)7));

    [Fact]
    public void Move_StampsTheTimeOfEachStep()
    {
        var load = new Load { ReferenceNo = "T-1", Origin = "A", Destination = "B" };
        var t1 = new DateTime(2026, 10, 23, 8, 0, 0, DateTimeKind.Utc);
        var t2 = t1.AddHours(5);

        LoadWorkflow.Move(load, LoadStatus.PickedUp, t1);
        LoadWorkflow.Move(load, LoadStatus.Delivered, t2);

        Assert.Equal(LoadStatus.Delivered, load.Status);
        Assert.Equal(t1, load.PickedUpAtUtc);
        Assert.Equal(t2, load.DeliveredAtUtc);
    }

    [Fact]
    public void Move_NotAllowed_ThrowsAndLeavesLoadUnchanged()
    {
        var load = new Load { ReferenceNo = "T-1", Origin = "A", Destination = "B" };

        Assert.Throws<InvalidOperationException>(() => LoadWorkflow.Move(load, LoadStatus.Delivered, DateTime.UtcNow));
        Assert.Equal(LoadStatus.Assigned, load.Status);
        Assert.Null(load.DeliveredAtUtc);
    }
}
