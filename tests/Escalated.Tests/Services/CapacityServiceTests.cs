using Escalated.Services;
using Xunit;

namespace Escalated.Tests.Services;

public class CapacityServiceTests
{
    [Fact]
    public async Task CanAcceptTicketAsync_ReturnsTrueWhenUnderCapacity()
    {
        var db = TestHelpers.CreateInMemoryDb();
        var service = new CapacityService(db);

        var canAccept = await service.CanAcceptTicketAsync(1);

        Assert.True(canAccept);
    }

    [Fact]
    public async Task IncrementAndDecrement_UpdatesCount()
    {
        var db = TestHelpers.CreateInMemoryDb();
        var service = new CapacityService(db);

        await service.IncrementLoadAsync(1);
        await service.IncrementLoadAsync(1);

        var capacities = await service.GetAllCapacitiesAsync();
        Assert.Single(capacities);
        Assert.Equal(2, capacities[0].CurrentCount);

        await service.DecrementLoadAsync(1);
        capacities = await service.GetAllCapacitiesAsync();
        Assert.Equal(1, capacities[0].CurrentCount);
    }

    [Fact]
    public async Task CanAcceptTicketAsync_ReturnsFalseWhenAtCapacity()
    {
        var db = TestHelpers.CreateInMemoryDb();
        var service = new CapacityService(db);

        await service.SetMaxConcurrentAsync(1, 2);
        await service.IncrementLoadAsync(1);
        await service.IncrementLoadAsync(1);

        var canAccept = await service.CanAcceptTicketAsync(1);

        Assert.False(canAccept);
    }

    [Fact]
    public async Task DecrementLoadAsync_DoesNotGoBelowZero()
    {
        var db = TestHelpers.CreateInMemoryDb();
        var service = new CapacityService(db);

        await service.DecrementLoadAsync(1);

        var capacities = await service.GetAllCapacitiesAsync();
        Assert.Equal(0, capacities[0].CurrentCount);
    }
}
