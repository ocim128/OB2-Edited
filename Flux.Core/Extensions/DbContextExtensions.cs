using Microsoft.EntityFrameworkCore;
using Flux.Core.Entities;
using System.Linq;

namespace Flux.Core.Extensions;

public static class DbContextExtensions
{
    public static void DetachLocal<T>(this DbContext context, int id) where T : Entity
    {
        var local = context.Set<T>().Local.FirstOrDefault(entry => entry.Id == id);
        
        if (local is not null)
        {
            context.Entry(local).State = EntityState.Detached;
        }
    }
}
