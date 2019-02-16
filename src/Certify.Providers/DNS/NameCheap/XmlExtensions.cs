﻿using System;
using System.Xml.Linq;

namespace NameCheap
{
    public static class XmlExtensions
    {
        /// <summary>
        /// Returns the parsed value of an attribute.
        /// </summary>
        public static T Attr<T>(this XElement xml, string attrName)
        {
            try
            {
                return (T) Convert.ChangeType(xml.Attribute(attrName)?.Value, typeof(T));
            }
            catch
            {
                return default(T);
            }
        }
    }
}
