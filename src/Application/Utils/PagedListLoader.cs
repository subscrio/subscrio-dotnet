using Subscrio.Core.Application.Constants;

namespace Subscrio.Core.Application.Utils;

/// <summary>
/// Loads every page from a limit/offset list API.
/// </summary>
public static class PagedListLoader
{
    public static async Task<List<T>> LoadAllAsync<T>(
        Func<int, int, Task<List<T>>> fetchPage,
        int pageSize = ApplicationConstants.MaxPageSize)
    {
        var all = new List<T>();
        var offset = 0;
        while (true)
        {
            var page = await fetchPage(offset, pageSize);
            all.AddRange(page);
            if (page.Count < pageSize)
            {
                break;
            }
            offset += pageSize;
        }
        return all;
    }
}
