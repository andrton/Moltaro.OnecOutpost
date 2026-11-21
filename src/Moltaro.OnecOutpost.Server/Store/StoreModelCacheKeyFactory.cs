using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace Moltaro.OnecOutpost.Server.Store;

// The key under which EF Core caches the model of a StoreContext. The model holds the schema, so the key does too:
// without it, the model built for the first schema would serve every other one. The model of the design time, which
// the check of pending model changes compares with the snapshot, is cached apart from the one of the run time.
internal sealed class StoreModelCacheKeyFactory : IModelCacheKeyFactory
{
    public object Create(DbContext context, bool designTime)
    {
        StoreContext store = (StoreContext)context;

        return (store.GetType(), store.Schema, designTime);
    }
}
