namespace KnowledgeBase.Core.RealTime;

public static class RealTimeRegistration
{
    // Singleton: publishers and open streams have to meet in the same instance, and it holds
    // process-wide state. One instance of the app, one user - no coordination between servers
    // is needed, which is exactly what would change if there were ever two.
    public static IServiceCollection AddRealTimeUpdates(this IServiceCollection services) =>
        services.AddSingleton<IChangeNotifier, ChangeNotifier>();
}
