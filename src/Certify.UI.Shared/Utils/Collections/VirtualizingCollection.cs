using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing.Printing;
using System.Linq;
using System.Threading.Tasks;
using Certify.Models;
using Certify.UI.Shared.Utils.Collections.Virtualization;

namespace Certify.UI.Shared.Utils.Collections
{
    public class ManagedCertificateProvider : IItemsProvider<ManagedCertificate>
    {
        private readonly int _count;

        private Func<int, int, ManagedCertificateFilter, Task<ManagedCertificateSearchResult>> _pageFetcher;

        private ManagedCertificateFilter _filter = new ManagedCertificateFilter();

        /// <summary>
        /// Initializes a new instance of the <see cref="DemoCustomerProvider"/> class.
        /// </summary>
        /// <param name="count">The count.</param>
        /// <param name="fetchDelay">The fetch delay.</param>
        public ManagedCertificateProvider(int count, Func<int, int, ManagedCertificateFilter, Task<ManagedCertificateSearchResult>> pageFetcher)
        {
            _count = count;
            _pageFetcher = pageFetcher;
        }

        /// <summary>
        /// Fetches the total number of items available.
        /// </summary>
        /// <returns></returns>
        public int FetchCount()
        {
            Trace.WriteLine("FetchCount");
            var task = _pageFetcher.Invoke(0, 1, _filter).ConfigureAwait(false);
            var result = task.GetAwaiter().GetResult();
            return (int)result.TotalResults;
        }

        public IList<ManagedCertificate> FetchRange(int pageIndex, int pageSize, out int overallCount)
        {
            Trace.WriteLine($"FetchRange: page {pageIndex} {pageSize}");

            var task = _pageFetcher.Invoke(pageIndex, pageSize, _filter).ConfigureAwait(false);

            var result = task.GetAwaiter().GetResult();
            overallCount = (int)result.TotalResults;
            
            return result.Results.ToList();
        }
    }
}
