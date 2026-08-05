using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using Newtonsoft.Json;

namespace Certify.Models
{
    /// <summary>
    /// Base class for data classes used with WPF, with a bubbled IsChanged property 
    /// </summary>
    /// <remarks>
    /// Handles any level of nested INotifyPropertyChanged objects (ex: other BindableBase- derived
    /// classes) or INotifyCollectionChanged objects (ex: ObservableCollection)
    /// </remarks>
    public class BindableBase : INotifyPropertyChanged
    {
        /// <summary>
        /// change notification provide by fody on compile, not that subclasses shouldn't inherit 
        /// </summary>
        public event PropertyChangedEventHandler? PropertyChanged;
        public event EventHandler? AfterPropertyChanged;

        private bool _isChangeDetectionPaused;

        public void PauseChangeEvents()
        {
            _isChangeDetectionPaused = true;
        }

        public void ResumeChangeEvents()
        {
            _isChangeDetectionPaused = false;
        }

        public void OnPropertyChanged(string prop, object before, object after)
        {
            if (_isChangeDetectionPaused)
            {
                return;
            }

            if (prop != nameof(IsChanged))
            {
                // auto-update the IsChanged property for standard properties
#if DEBUG
                // System.Diagnostics.Debug.WriteLine($"Model change: {prop} from {before} to {after}");
#endif
                if (before != after)
                {
                    IsChanged = true;
                }
            }

            // maintain direct-child subscriptions when a trackable reference is swapped
            if (!ReferenceEquals(before, after))
            {
                if (IsTrackable(before))
                {
                    UnsubscribeChild(before);
                }

                if (IsTrackable(after))
                {
                    AttachToChild(after, new HashSet<object>(ReferenceEqualityComparer.Instance));
                }
            }

            // fire the event
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(prop));

            // optional handler after property change completed (saving etc)
            AfterPropertyChanged?.Invoke(this, new PropertyChangedEventArgs(prop));
        }

        private static bool IsTrackable(object? value)
        {
            if (value is null || value is string)
            {
                return false;
            }

            return value is INotifyPropertyChanged || value is INotifyCollectionChanged || value is ICollection;
        }

        private void AttachHandlers(object child)
        {
            // attach to INotifyPropertyChanged children
            if (child is INotifyPropertyChanged prop)
            {
                prop.PropertyChanged -= OnChildChanged;
                prop.PropertyChanged += OnChildChanged;
            }
            // attach to INotifyCollectionChanged children
            if (child is INotifyCollectionChanged coll)
            {
                coll.CollectionChanged -= OnChildChanged;
                coll.CollectionChanged += OnChildChanged;
            }
        }

        private void DetachHandlers(object child)
        {
            // detach from INotifyPropertyChanged children
            if (child is INotifyPropertyChanged prop)
            {
                prop.PropertyChanged -= OnChildChanged;
            }
            // detach from INotifyCollectionChanged children
            if (child is INotifyCollectionChanged coll)
            {
                coll.CollectionChanged -= OnChildChanged;
            }
        }

        /// <summary>
        /// Handler attached only to this object's direct children. Any reported child change marks this
        /// object as changed; the woven IsChanged notification is in turn heard by this object's own parent,
        /// so the dirty signal bubbles up to the root one hop at a time.
        /// </summary>
        private void OnChildChanged(object? src, EventArgs args)
        {
            if (_isChangeDetectionPaused)
            {
                return;
            }

            if (args is NotifyCollectionChangedEventArgs ccArgs)
            {
                if (ccArgs.OldItems != null)
                {
                    foreach (var obj in ccArgs.OldItems)
                    {
                        UnsubscribeChild(obj);
                    }
                }

                if (ccArgs.NewItems != null)
                {
                    foreach (var obj in ccArgs.NewItems)
                    {
                        AttachToChild(obj, new HashSet<object>(ReferenceEqualityComparer.Instance));
                    }
                }
            }

            IsChanged = true;
        }

        private void EstablishSubscriptions() => EstablishSubscriptions(new HashSet<object>(ReferenceEqualityComparer.Instance));

        /// <summary>
        /// (Re)attaches this object's change handler to its direct children and asks each nested model to
        /// wire up its own children, (re)establishing change tracking across an existing object graph.
        /// </summary>
        private void EstablishSubscriptions(HashSet<object> visited)
        {
            if (!visited.Add(this))
            {
                return;
            }

            foreach (var child in GetTrackableChildren())
            {
                AttachToChild(child, visited);
            }
        }

        /// <summary>
        /// Attaches this object's handler to a direct child (and, for a child collection, to each of its
        /// items); nested BindableBase children are asked to wire up their own direct children in turn.
        /// </summary>
        private void AttachToChild(object? child, HashSet<object> visited)
        {
            if (child is null || child is string)
            {
                return;
            }

            AttachHandlers(child);

            if (child is ICollection collection)
            {
                var snapshot = new List<object?>();
                foreach (var item in collection)
                {
                    snapshot.Add(item);
                }

                foreach (var item in snapshot)
                {
                    AttachToChild(item, visited);
                }
            }
            else if (child is BindableBase bindableChild)
            {
                bindableChild.EstablishSubscriptions(visited);
            }
        }

        private void UnsubscribeChild(object? child)
        {
            if (child is null || child is string)
            {
                return;
            }

            DetachHandlers(child);

            if (child is ICollection collection)
            {
                foreach (var item in collection)
                {
                    if (item is not null && item is not string)
                    {
                        DetachHandlers(item);
                    }
                }
            }
        }

        /// <summary>
        /// Returns this object's direct property values that can participate in change tracking
        /// (INotifyPropertyChanged / INotifyCollectionChanged / ICollection), excluding strings.
        /// </summary>
        private IEnumerable<object> GetTrackableChildren()
        {
            foreach (var property in GetType().GetProperties())
            {
                if (!property.CanRead || property.GetIndexParameters().Length != 0)
                {
                    continue;
                }

                var type = property.PropertyType;
                if (type == typeof(string)
                    || (!typeof(INotifyPropertyChanged).IsAssignableFrom(type)
                        && !typeof(INotifyCollectionChanged).IsAssignableFrom(type)
                        && !typeof(ICollection).IsAssignableFrom(type)))
                {
                    continue;
                }

                object? value;
                try
                {
                    value = property.GetValue(this);
                }
                catch
                {
                    continue;
                }

                if (value is not null)
                {
                    yield return value;
                }
            }
        }

        private sealed class ReferenceEqualityComparer : IEqualityComparer<object>
        {
            public static ReferenceEqualityComparer Instance { get; } = new();

            public new bool Equals(object? x, object? y) => ReferenceEquals(x, y);

            public int GetHashCode(object obj) => RuntimeHelpers.GetHashCode(obj);
        }

        public void RaisePropertyChangedEvent(string prop) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(prop));

        /// <summary>
        /// True if a property has been changed on the model since IsChanged was last set to false 
        /// </summary>
        [JsonIgnore] // don't deserialize this property from legacy saved settings
        public bool IsChanged
        {
            get => isChanged;
            set
            {
                if (!value)
                {
                    SetClean();
                }
                else if (!isChanged)
                {
                    // raise only on the false->true transition so the change bubbles to our parent
                    // exactly once and cannot loop back down through a cyclic graph
                    isChanged = true;
                    RaisePropertyChangedEvent(nameof(IsChanged));
                }
            }
        }

        private bool isChanged;

        /// <summary>
        /// If an action/event will have modified IsChanged but the change should be ignored, reset the value
        /// </summary>
        /// <param name="val"></param>
        public void ResetIsChanged(bool val)
        {
            if (val)
            {
                isChanged = true;
            }
            else
            {
                SetClean();
            }

            RaisePropertyChangedEvent(nameof(IsChanged));
        }

        /// <summary>
        /// Clears IsChanged on this object and every nested model, then (re)establishes change tracking
        /// subscriptions for the current object graph so future nested changes are detected.
        /// </summary>
        private void SetClean()
        {
            ClearChangedFlags(this, new HashSet<object>(ReferenceEqualityComparer.Instance));
            EstablishSubscriptions();
        }

        /// <summary>
        /// recursively unsets IsChanged on a BindableBase object, any property on the object of type
        /// BindableBase, and any BindableBase objects nested in ICollection properties
        /// </summary>
        private static void ClearChangedFlags(object? node, HashSet<object> visited)
        {
            if (node is null || node is string || !visited.Add(node))
            {
                return;
            }

            if (node is BindableBase bb)
            {
                bb.isChanged = false;

                foreach (var child in bb.GetTrackableChildren())
                {
                    ClearChangedFlags(child, visited);
                }
            }
            else if (node is ICollection collection)
            {
                foreach (var subObj in collection)
                {
                    ClearChangedFlags(subObj, visited);
                }
            }
        }
    }
}
