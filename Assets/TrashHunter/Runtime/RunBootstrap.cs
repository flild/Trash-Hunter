using UnityEngine;
using VContainer;
using VContainer.Unity;

namespace TrashHunter
{
    internal static class RunBootstrap
    {
        public static LifetimeScope RunScope { get; private set; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void Reset() => RunScope = null;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Start()
        {
            var catalog = LootCatalog.Load();
            var application = LifetimeScope.Create(builder => builder.RegisterInstance(catalog), "Application Scope");
            Object.DontDestroyOnLoad(application.gameObject);
            RunScope = application.CreateChild(builder =>
                builder.Register(resolver => new GameState(resolver.Resolve<LootCatalog>()), Lifetime.Singleton), "Run Scope");
            RunScope.Container.Resolve<GameState>().LoadSlot();
        }
    }
}
