using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using Certify.Models.Config;

namespace Certify.UI.Windows
{
    /// <summary>
    /// Selects template based on the type of the data item. 
    /// </summary>
    public class ControlTemplateSelector : DataTemplateSelector
    {
        public override DataTemplate SelectTemplate(object item, DependencyObject container)
        {
            var context = container as FrameworkElement;
            DataTemplate template = null;

            if (null == container)
            {
                throw new NullReferenceException("container");
            }
            else if (null == context)
            {
                throw new Exception("container must be FramekworkElement");
            }
            else if (null == item)
            {
                return null;
            }

            template = null;

            var providerParameter = item as ProviderParameter;
            if (providerParameter == null)
            {
                template = context.FindResource("ProviderStringParameter") as DataTemplate;
            }
            else if (providerParameter.IsPassword)
            {
                template = context.FindResource("ProviderPasswordParameter") as DataTemplate;
            }
            else if (providerParameter.Options.Count() != 0)
            {
                template = context.FindResource("ProviderDropDownParameter") as DataTemplate;
            }
            else
            {
                template = context.FindResource("ProviderStringParameter") as DataTemplate;
            }

            return template ?? base.SelectTemplate(item, container);
        }
    }
}
