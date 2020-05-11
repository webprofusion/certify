using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Certify.Locales;
using Certify.UI.Utils;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Certify.UI.Tests.Integration
{
    [TestClass]

    public class ConverterTests
    {

        [TestMethod]
        public void ExpiryDateConvertDescription()
        {
            // expires in 8 days
            var description = ExpiryDateConverter.GetDescription(DateTime.Now.AddDays(8.1));

            Assert.AreEqual(string.Format(SR.ExpiryDateConverter_CertificateExpiresIn, 8), description);


            // expired 1 days ago

            description = ExpiryDateConverter.GetDescription(DateTime.Now.AddDays(-1));

            Assert.AreEqual(string.Format(SR.ExpiryDateConverter_CertificateExpiredNDaysAgo, 1), description);

            // null expiry

            description = ExpiryDateConverter.GetDescription(null);

            Assert.AreEqual(SR.ExpiryDateConverter_NoCurrentCertificate, description);

        }


        [TestMethod]
        public void ExpiryDateConvertColour()
        {
            // 7 days to go, should be orange
            var color = ExpiryDateColourConverter.GetColour(DateTime.Now.AddDays(7.1));

            Assert.AreEqual(System.Windows.Media.Brushes.Orange, color);

            // 6 days to go, should be red
            color = ExpiryDateColourConverter.GetColour(DateTime.Now.AddDays(6.1));

            Assert.AreEqual(System.Windows.Media.Brushes.Red, color);


            // 0 days to go (less than 1), should be red
            color = ExpiryDateColourConverter.GetColour(DateTime.Now.AddDays(1.1));

            Assert.AreEqual(System.Windows.Media.Brushes.Red, color);

            // expired, more than 0 days past expiry, should be dark red
            color = ExpiryDateColourConverter.GetColour(DateTime.Now.AddDays(-1));

            Assert.AreEqual(System.Windows.Media.Brushes.DarkRed, color);

            // still plenty of time remaining, should be green
            color = ExpiryDateColourConverter.GetColour(DateTime.Now.AddDays(30));

            Assert.AreEqual(System.Windows.Media.Brushes.Green, color);

            // null expiry
            color = ExpiryDateColourConverter.GetColour(null);

            Assert.AreEqual(System.Windows.Media.Brushes.SlateGray, color);

        }
    }
}
