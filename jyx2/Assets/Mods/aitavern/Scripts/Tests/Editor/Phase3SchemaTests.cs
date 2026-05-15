// T3A.4 (Phase 3A) — schema deserialize / default-init fixture.
//
// Plan §9 ("WorldCodex / Dossier deserialize") + §11 ("extended CharacterBio
// fields default empty; Phase 1/2 .asset still deserialize — Unity
// default-inits new serialized fields"). These are ScriptableObjects, so a
// fresh ScriptableObject.CreateInstance<T>() is the in-editor equivalent of
// loading an asset whose serialized data is empty (which is exactly what an
// un-pipelined / Phase-1-era asset looks like for the new fields).
//
// The load-bearing assertion: every List field is non-null after construction
// (the runtime ContextAssembler iterates them WITHOUT null guards on the list
// itself in several spots — e.g. d.PolityKnowledge — so a null list would NRE
// the assembler). CharacterBio's new Phase 3 fields must be at type defaults
// and must not perturb the existing Phase 1/2 fields.
using NUnit.Framework;
using UnityEngine;

namespace Jyx2.AITavern.Tests
{
    [TestFixture]
    public class Phase3SchemaTests
    {
        WorldCodex _world;
        CharacterDossier _dossier;
        CharacterBio _bio;

        [TearDown]
        public void TearDown()
        {
            if (_world != null) Object.DestroyImmediate(_world);
            if (_dossier != null) Object.DestroyImmediate(_dossier);
            if (_bio != null) Object.DestroyImmediate(_bio);
        }

        // ---------- Test 1: fresh SOs default-init, no NRE on new fields ----------

        [Test]
        public void Schema_NewSOs_DefaultInitializedNoNRE()
        {
            _world = ScriptableObject.CreateInstance<WorldCodex>();
            _dossier = ScriptableObject.CreateInstance<CharacterDossier>();
            _bio = ScriptableObject.CreateInstance<CharacterBio>();

            // WorldCodex — every list field non-null (assembler iterates these).
            Assert.IsNotNull(_world.Polities, "WorldCodex.Polities must default to a non-null list");
            Assert.IsNotNull(_world.Factions, "WorldCodex.Factions must default to a non-null list");
            Assert.IsNotNull(_world.NotableFigures, "WorldCodex.NotableFigures must default to a non-null list");
            Assert.AreEqual(0, _world.Polities.Count, "fresh WorldCodex has no polities");
            Assert.AreEqual(0, _world.Factions.Count, "fresh WorldCodex has no factions");
            Assert.AreEqual(0, _world.NotableFigures.Count, "fresh WorldCodex has no notable figures");
            Assert.IsTrue(string.IsNullOrEmpty(_world.Era), "fresh WorldCodex Era empty");

            // CharacterDossier — every list field non-null.
            Assert.IsNotNull(_dossier.PolityKnowledge, "CharacterDossier.PolityKnowledge non-null");
            Assert.IsNotNull(_dossier.FactionKnowledge, "CharacterDossier.FactionKnowledge non-null");
            Assert.IsNotNull(_dossier.People, "CharacterDossier.People non-null");
            Assert.AreEqual(0, _dossier.PolityKnowledge.Count);
            Assert.AreEqual(0, _dossier.FactionKnowledge.Count);
            Assert.AreEqual(0, _dossier.People.Count);
            Assert.IsTrue(string.IsNullOrEmpty(_dossier.AgentId), "fresh Dossier AgentId empty");

            // CharacterBio — new Phase 3 fields at type defaults.
            Assert.AreEqual(Sex.Male, _bio.Sex, "Sex enum default is the zero member (Male)");
            Assert.IsTrue(string.IsNullOrEmpty(_bio.AgeText), "AgeText default empty");
            Assert.IsTrue(string.IsNullOrEmpty(_bio.Personality), "Personality default empty");
            Assert.IsTrue(string.IsNullOrEmpty(_bio.Appearance), "Appearance default empty");
            Assert.IsTrue(string.IsNullOrEmpty(_bio.SurfaceManner), "SurfaceManner default empty");

            // Existing Phase 1/2 fields unaffected (still their own defaults —
            // additive-only migration, Plan §11).
            Assert.IsTrue(string.IsNullOrEmpty(_bio.AgentId), "existing AgentId default empty");
            Assert.IsTrue(string.IsNullOrEmpty(_bio.BioName), "existing BioName default empty");
            Assert.IsTrue(string.IsNullOrEmpty(_bio.Identity), "existing Identity default empty");
            Assert.IsTrue(string.IsNullOrEmpty(_bio.Plans), "existing Plans default empty");
            Assert.IsNotNull(_bio.Relationships, "existing Relationships list non-null");
            Assert.IsNotNull(_bio.Interests, "existing Interests list non-null");
            Assert.IsNotNull(_bio.StartingItems, "existing StartingItems list non-null");
            Assert.AreEqual(0, _bio.RoleId, "existing RoleId default 0");
            Assert.AreEqual(0, _bio.HeadId, "existing HeadId default 0");

            // The actual NRE guard: feeding these fresh SOs through the
            // assembler must not throw (empty lists, null strings everywhere).
            Assert.DoesNotThrow(() =>
            {
                var mgr = TestBuilders.MakeManager();
                try
                {
                    mgr.World = _world;
                    mgr.Dossiers["t"] = _dossier;
                    var talker = TestBuilders.MakeAgent("t", bio: _bio);
                    ContextAssembler.Build(talker, null, mgr, 0, ContextProfile.Full);
                }
                finally
                {
                    TestBuilders.DestroyManager(mgr);
                }
            }, "assembler must not NRE on freshly-constructed (all-empty) lore SOs");
        }
    }
}
