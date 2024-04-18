using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Certify.Models;

namespace Certify.UI.Shared.Utils
{

    public class ManagedCertificateVirtualObservableCollection : VirtualObservableCollection<ManagedCertificate>
    {
        public ManagedCertificateVirtualObservableCollection(int totalItems, int pageSize, Func<int, int, Task<IEnumerable<ManagedCertificate>>> pageFetcher) : base(totalItems, pageSize, pageFetcher)
        {
        }

        public override void AddOrUpdate(ManagedCertificate item)
        {
            if (_items.Any(i => i.Id == item.Id))
            {
                _items.Remove(_items.First(i => i.Id == item.Id));
            }

            _items.Add(item);
        }
    }
    public class VirtualObservableCollection<T> : IEnumerable, INotifyCollectionChanged, INotifyPropertyChanged

    {
        private int _pageSize;
        private long _totalItems;
        private readonly Func<int, int, Task<IEnumerable<T>>> _pageFetcher;
        protected readonly ObservableCollection<T> _items;

        public event PropertyChangedEventHandler PropertyChanged;

        public VirtualObservableCollection(long totalItems, int pageSize, Func<int, int, Task<IEnumerable<T>>> pageFetcher)
        {
            _totalItems = totalItems;
            _pageSize = pageSize;
            _pageFetcher = pageFetcher;
            _items = new ObservableCollection<T>();
        }

        public IEnumerator<T> GetEnumerator()
        {
            return _items.GetEnumerator();
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }

        public void Add(T item)
        {
            throw new NotSupportedException();
        }

        public void Clear()
        {
            throw new NotSupportedException();
        }

        public bool Contains(T item)
        {
            return _items.Contains(item);
        }

        public void CopyTo(T[] array, int arrayIndex)
        {
            _items.CopyTo(array, arrayIndex);
        }

        public bool Remove(T item)
        {
            throw new NotSupportedException();
        }

        public int Count => _items.Count;

        public bool IsReadOnly => true;

        public int IndexOf(T item)
        {
            return _items.IndexOf(item);
        }

        public void Insert(int index, T item)
        {
            throw new NotSupportedException();
        }

        public void RemoveAt(int index)
        {
            throw new NotSupportedException();
        }

        public T this[int index]
        {
            get
            {
                int pageIndex = index / _pageSize;
                int pageOffset = index % _pageSize;

                while (_items.Count <= index)
                {
                    var page = _pageFetcher(pageIndex++, _pageSize).Result;
                    foreach (var item in page)
                    {
                        _items.Add(item);
                    }
                }

                return _items[index];
            }
            set => throw new NotSupportedException();
        }

        public T FirstOrDefault(Func<T, bool> value = null)
        {
            return this[0];
        }
        public T Last()
        {
            return this[this.Count - 1];
        }

        public virtual void AddOrUpdate(T item)
        {

            throw new NotSupportedException();

        }
        internal void UpdatePage(long totalItems, IEnumerable<T> pageResults, int filterPageIndex, int filterPageSize)
        {
            _totalItems = totalItems;
            foreach (var item in pageResults)
            {
                _items.Add(item);
            }
        }

        public event NotifyCollectionChangedEventHandler CollectionChanged
        {
            add => _items.CollectionChanged += value;
            remove => _items.CollectionChanged -= value;
        }
    }
}
