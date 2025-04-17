using System;
using System.Collections.Generic;
using System.Linq;
using Certify.Models;
using Certify.Models.Shared.Validation;

namespace Certify.Shared.Core.Utils
{
    public class DemoDataGenerator
    {
        public static List<ManagedCertificate> GenerateDemoItems(int min = 500, int max = 500)
        {
            var rnd = new Random();

            var items = new List<ManagedCertificate>();
            var numItems = rnd.Next(min, max);
            for (var i = 0; i < numItems; i++)
            {

                var item = new ManagedCertificate
                {
                    Id = Guid.NewGuid().ToString(),
                    Name = GenerateName(rnd),
                    RequestConfig = new CertRequestConfig
                    {
                        Challenges = new System.Collections.ObjectModel.ObservableCollection<CertRequestChallengeConfig> { new CertRequestChallengeConfig { ChallengeType = SupportedChallengeTypes.CHALLENGE_TYPE_HTTP } }
                    }
                };

                item.DomainOptions.Add(new DomainOption { Domain = $"{item.Name}.dev.projectbids.co.uk", IsManualEntry = true, IsPrimaryDomain = true, IsSelected = true, Type = CertIdentifierType.Dns });
                item.RequestConfig.PrimaryDomain = item.DomainOptions[0].Domain;
                item.RequestConfig.SubjectAlternativeNames = new string[] { item.DomainOptions[0].Domain };

                var validation = CertificateEditorService.Validate(item, null, null, applyAutoConfiguration: true);
                if (validation.IsValid)
                {
                    var demoState = new Random().Next(1, 3);
                    var certLifetime = new Random().Next(7, 30);
                    var certElapsed = new Random().Next(1, certLifetime);
                    var certStart = DateTime.UtcNow.AddDays(-certElapsed);

                    if (demoState == 1)
                    {
                        // not yet requested
                        item.Comments = "[DemoData-1]";
                    }
                    else if (demoState == 2)
                    {
                        // failed
                        item.CertificateCurrentCA = "demo-ca.org";
                        item.DateStart = certStart;
                        item.DateLastRenewalAttempt = certStart;
                        item.DateExpiry = certStart.AddDays(certLifetime);
                        item.CertificateFriendlyName = $"{item.GetCertificateIdentifiers().First().Value} [CertifyDemo] - {item.DateStart} to {item.DateExpiry}";
                        item.Comments = "[DemoData-2] This is an example item showing failure.";
                        item.LastAttemptedCA = item.CertificateCurrentCA;
                        item.LastRenewalStatus = RequestState.Error;
                        item.RenewalFailureCount = new Random().Next(1, 3);
                        item.RenewalFailureMessage = "Item failed because it is a demo item that was designed to show what failure looks like.";

                    }
                    else if (demoState == 3)
                    {
                        //success
                        item.CertificateCurrentCA = "demo-ca.org";
                        item.DateStart = certStart;
                        item.DateLastRenewalAttempt = certStart;
                        item.DateExpiry = certStart.AddDays(certLifetime);
                        item.CertificateFriendlyName = $"{item.GetCertificateIdentifiers().First().Value} [CertifyDemo] - {item.DateStart} to {item.DateExpiry}";
                        item.Comments = "[DemoData-3] This is an example item showing success";
                        item.LastAttemptedCA = item.CertificateCurrentCA;
                        item.LastRenewalStatus = RequestState.Success;
                    }

                    items.Add(item);
                }
                else
                {
                    // generated invalid test item
                    System.Diagnostics.Debug.WriteLine(validation.Message);
                }
            }

            return items;
        }

        public static string GenerateName(Random rnd)
        {
            // generate test item names using verb,animal
            var subjects = new string[] {
                    "Lion",
                    "Tiger",
                    "Leopard",
                    "Cheetah",
                    "Elephant",
                    "Giraffe",
                    "Rhinoceros",
                    "Gorilla",
                    "Chimpanzee",
                    "Quenda",
                    "Koala",
                    "Panda",
                    "Kangaroo",
                    "Wallaby",
                    "Platypus",
                    "Wombat",
                    "Dingo",
                    "Emu",
                    "Cassowary",
                    "Cockatoo",
                    "Echidna",
                    "TasmanianDevil",
                    "Bilby",
                    "Bandicoot",
                    "Numbat",
                    "SugarGlider",
                    "Possum",
                    "Lyrebird",
                    "Brolga",
                    "Magpie",
                    "Kookaburra",
                    "FrillNeckLizard",
                    "Goanna",
                    "ThornyDevil",
                    "BlueTongueSkink",
                    "SaltwaterCrocodile",
                    "FreshwaterCrocodile",
                    "Dugong",
                    "SeaTurtle",
                    "WhaleShark",
                    "GreatWhiteShark",
                    "HammerheadShark",
                    "MantaRay",
                    "Stingray",
                    "Clownfish",
                    "Seahorse",
                    "Octopus",
                    "Squid",
                    "Jellyfish",
                    "Starfish",
                    "Coral",
                    "Dolphin",
                    "Orca",
                    "HumpbackWhale",
                    "BlueWhale",
                    "Penguin",
                    "Albatross",
                    "Seal",
                    "SeaLion",
                    "Walrus",
                    "PolarBear",
                    "ArcticFox",
                    "SnowLeopard",
                    "Moose",
                    "Caribou",
                    "Bison",
                    "Beaver",
                    "Otter",
                    "Lynx",
                    "Bobcat",
                    "Cougar",
                    "Jaguar",
                    "Ocelot",
                    "Sloth",
                    "Armadillo",
                    "Anteater",
                    "Capybara",
                    "Tapir",
                    "Peccary",
                    "HowlerMonkey",
                    "SpiderMonkey",
                    "Tamarin",
                    "Marmoset",
                    "Macaw",
                    "Toucan",
                    "HarpyEagle",
                    "Anaconda",
                    "BoaConstrictor",
                    "Caiman",
                    "Piranha",
                    "ElectricEel",
                    "Axolotl",
                    "Salamander",
                    "Newt",
                    "Frog",
                    "Toad"
                };

            var adjectives = new string[] {
                "active",
                "adaptable",
                "alert",
                "clever",
                "comfortable",
                "conscientious",
                "considerate",
                "courageous",
                "decisive",
                "determined",
                "diligent",
                "energetic",
                "entertaining",
                "enthusiastic",
                "fabulous",
                "friendly",
                "generous",
                "gentle",
                "graceful",
                "hardworking",
                "helpful",
                "honest",
                "imaginative",
                "independent",
                "intelligent",
                "kind",
                "lively",
                "loyal",
                "modest",
                "optimistic",
                "organized",
                "patient",
                "persistent",
                "polite",
                "practical",
                "punctual",
                "reliable",
                "resourceful",
                "respectful",
                "responsible",
                "sincere",
                "skillful",
                "sociable",
                "supportive",
                "thoughtful",
                "trustworthy",
                "understanding",
                "versatile",
                "vibrant",
                "witty",
                "zealous",
                "adventurous",
                "ambitious",
                "artistic",
                "athletic",
                "attentive",
                "bold",
                "brave",
                "calm",
                "charismatic",
                "cheerful",
                "compassionate",
                "confident",
                "creative",
                "curious",
                "dedicated",
                "dependable",
                "dynamic",
                "eager",
                "efficient",
                "empathetic",
                "enthusiastic",
                "flexible",
                "forgiving",
                "funny",
                "generous",
                "grateful",
                "humble",
                "inspiring",
                "inventive",
                "joyful",
                "motivated",
                "observant",
                "outgoing",
                "passionate",
                "perceptive",
                "playful",
                "proactive",
                "resilient",
                "selfless",
                "sensible",
                "spontaneous",
                "strategic",
                "tactful",
                "tenacious",
                "thoughtful",
                "tolerant",
                "visionary",
                "wise"
            };

            return $"{adjectives[rnd.Next(0, adjectives.Length - 1)]}-{subjects[rnd.Next(0, subjects.Length - 1)]}".ToLower();
        }
    }
}
