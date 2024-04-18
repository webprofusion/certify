using System;
using System.Collections;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;
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

            // hook up to events
            DetachChangeEventHandlers(before);
            AttachChangeEventHandlers(after);

            // fire the event
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(prop));

            // optional handler after property change completed (saving etc)
            AfterPropertyChanged?.Invoke(this, new PropertyChangedEventArgs(prop));
        }

        private void AttachChangeEventHandlers(object obj)
        {
            // attach to INotifyPropertyChanged properties
            if (obj is INotifyPropertyChanged prop)
            {
                prop.PropertyChanged += HandleChangeEvent;
            }
            // attach to INotifyCollectionChanged properties
            if (obj is INotifyCollectionChanged coll)
            {
                coll.CollectionChanged += HandleChangeEvent;
            }
        }

        private void DetachChangeEventHandlers(object obj)
        {
            // detach from INotifyPropertyChanged properties
            if (obj is INotifyPropertyChanged prop)
            {
                prop.PropertyChanged -= HandleChangeEvent;
            }
            // detach from INotifyCollectionChanged properties
            if (obj is INotifyCollectionChanged coll)
            {
                coll.CollectionChanged -= HandleChangeEvent;
            }
        }

        private void HandleChangeEvent(object src, EventArgs args)
        {
            if (_isChangeDetectionPaused)
            {
                return;
            }

            IsChanged = true;

            if (args is NotifyCollectionChangedEventArgs ccArgs)
            {
                if (ccArgs.Action == NotifyCollectionChangedAction.Remove)
                {
                    foreach (var obj in ccArgs.OldItems)
                    {
                        DetachChangeEventHandlers(obj);
                    }
                }

                if (ccArgs.Action == NotifyCollectionChangedAction.Add)
                {
                    foreach (var obj in ccArgs.NewItems)
                    {
                        AttachChangeEventHandlers(obj);
                    }
                }
            }
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
                    BindableBase.UnsetChanged(this);
                }
                else
                {
                    isChanged = value;
                }
            }
        }

        private bool isChanged;

        /// <summary>
        /// If an action/event will have modified IsChanged but the change should be ignored, reset the value without firing events
        /// </summary>
        /// <param name="val"></param>
        public void ResetIsChanged(bool val)
        {
            isChanged = val;
            RaisePropertyChangedEvent(nameof(isChanged));
        }

        /// <summary>
        /// recursively unsets IsChanged on a BindableBase object, any property on the object of type
        /// BindableBase, and any BindableBase objects nested in ICollection properties
        /// </summary>
        /// <param name="obj"></param>
        private static void UnsetChanged(object obj)
        {
            if (obj is BindableBase bb)
            {
                bb.isChanged = false;
                var props = obj.GetType().GetProperties();
                foreach (var prop in props.Where(p =>
                    typeof(ICollection).IsAssignableFrom(p.PropertyType) ||
                    p.PropertyType.IsSubclassOf(typeof(BindableBase))))
                {
                    var val = prop.GetValue(obj);
                    if (val is ICollection propertyCollection && prop.Name!="ManagedCertificates")
                    {
                        foreach (var subObj in propertyCollection)
                        {
                            BindableBase.UnsetChanged(subObj);
                        }
                    }

                    if (val is BindableBase bbSub)
                    {
                        BindableBase.UnsetChanged(bbSub);
                    }
                }
            }

            if (obj is ICollection collection)
            {
                foreach (var subObj in collection)
                {
                    BindableBase.UnsetChanged(subObj);
                }
            }
        }
    }
}
