using System;
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
            var description = ExpiryDateConverter.GetDescription(new Models.Lifetime(DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddDays(8.1)));

            Assert.AreEqual(string.Format(SR.ExpiryDateConverter_CertificateExpiresIn, 8), description);

            // expired 1 days ago

            description = ExpiryDateConverter.GetDescription(new Models.Lifetime(DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddDays(-1)));

            Assert.AreEqual(string.Format(SR.ExpiryDateConverter_CertificateExpiredNDaysAgo, 1), description);

            // null expiry

            description = ExpiryDateConverter.GetDescription(null);

            Assert.AreEqual(SR.ExpiryDateConverter_NoCurrentCertificate, description);

        }

        [TestMethod]
        public void ExpiryDateConvertColour()
        {
            var errorColour = System.Windows.Media.Brushes.DarkRed;
            var dangerColour = System.Windows.Media.Brushes.DarkRed;
            var warningColour = System.Windows.Media.Brushes.Chocolate;
            var successColour = System.Windows.Media.Brushes.Green;
            var inactiveColour = System.Windows.Media.Brushes.SlateGray;

            // 13 days to go, should be warning
            var color = ExpiryDateColourConverter.GetColour(new Models.Lifetime(DateTimeOffset.UtcNow.AddDays(-77), DateTimeOffset.UtcNow.AddDays(13)));

            Assert.AreEqual(warningColour, color);

            // 6 days to go, should be warning
            color = ExpiryDateColourConverter.GetColour(new Models.Lifetime(DateTimeOffset.UtcNow.AddDays(-(90 - 6.1)), DateTimeOffset.UtcNow.AddDays(6.1)));

            Assert.AreEqual(warningColour, color);

            // 0 days to go (less than 1), should be error
            color = ExpiryDateColourConverter.GetColour(new Models.Lifetime(DateTimeOffset.UtcNow.AddDays(-89), DateTimeOffset.UtcNow.AddDays(1.1)));

            Assert.AreEqual(dangerColour, color);

            // expired, more than 0 days past expiry, should be dark red
            color = ExpiryDateColourConverter.GetColour(new Models.Lifetime(DateTimeOffset.UtcNow.AddDays(-90), DateTimeOffset.UtcNow.AddDays(-1)));

            Assert.AreEqual(errorColour, color);

            // still plenty of time remaining, should be green
            color = ExpiryDateColourConverter.GetColour(new Models.Lifetime(DateTimeOffset.UtcNow.AddDays(-60), DateTimeOffset.UtcNow.AddDays(30.1)));

            Assert.AreEqual(successColour, color);

            // null expiry
            color = ExpiryDateColourConverter.GetColour(null);

            Assert.AreEqual(inactiveColour, color);

        }
    }
}
