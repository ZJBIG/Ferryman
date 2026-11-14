using System.Collections;
using Ferryman.Characters;
using Ferryman.World;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace Ferryman.Tests
{
    public sealed class PlayerMovementTests
    {
        private PlayerCharacter player;
        private Rigidbody2D body;
        private BoxCollider2D playerCollider;
        private BoatDeck deck;
        private SimulationMode2D originalSimulationMode;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            originalSimulationMode = Physics2D.simulationMode;
            yield return SceneManager.LoadSceneAsync("Prototype");
            player = Object.FindObjectOfType<PlayerCharacter>();
            deck = Object.FindObjectOfType<BoatDeck>();
            Assert.That(player, Is.Not.Null);
            Assert.That(deck, Is.Not.Null);
            body = player.GetComponent<Rigidbody2D>();
            playerCollider = player.GetComponent<BoxCollider2D>();
            player.enabled = false;
            body.interpolation = RigidbodyInterpolation2D.None;
            Physics2D.simulationMode = SimulationMode2D.Script;
            Physics2D.SyncTransforms();
        }

        [TearDown]
        public void TearDown()
        {
            Physics2D.simulationMode = originalSimulationMode;
        }

        [Test]
        public void NoInputKeepsPlayerStill()
        {
            Vector2 start = body.position;
            Step(Vector2.zero, 50);
            Assert.That(Vector2.Distance(body.position, start), Is.LessThan(0.001f));
        }

        [Test]
        public void MovementHasExpectedSpeedAndStopsOnRelease()
        {
            Vector2 start = body.position;
            Step(Vector2.right, 10);
            Assert.That(body.position.x - start.x, Is.EqualTo(0.8f).Within(0.001f));
            Vector2 stopped = body.position;
            Step(Vector2.zero, 10);
            Assert.That(Vector2.Distance(body.position, stopped), Is.LessThan(0.001f));
        }

        [Test]
        public void DiagonalMovementIsNotFaster()
        {
            Vector2 start = body.position;
            Step(Vector2.one, 10);
            Assert.That(Vector2.Distance(body.position, start), Is.EqualTo(0.8f).Within(0.001f));
        }

        [TestCase(1f, 0f)]
        [TestCase(-1f, 0f)]
        [TestCase(0f, 1f)]
        [TestCase(0f, -1f)]
        [TestCase(1f, 1f)]
        [TestCase(-1f, 1f)]
        [TestCase(1f, -1f)]
        [TestCase(-1f, -1f)]
        public void PlayerColliderStaysInsideAllEdgesAndCorners(float x, float y)
        {
            var direction = new Vector2(x, y);
            for (int i = 0; i < 250; i++)
            {
                Step(direction, 1);
                AssertInsideDeck();
            }

            Bounds boundary = deck.GetComponent<EdgeCollider2D>().bounds;
            if (x != 0f)
                Assert.That(Mathf.Abs(body.position.x), Is.GreaterThan(boundary.extents.x - playerCollider.bounds.extents.x - 0.1f));
            if (y != 0f)
                Assert.That(Mathf.Abs(body.position.y), Is.GreaterThan(boundary.extents.y - playerCollider.bounds.extents.y - 0.1f));
        }

        [Test]
        public void PlayerCanSlideAlongWallAndMoveAway()
        {
            Step(Vector2.up, 100);
            Vector2 atWall = body.position;
            Step(Vector2.one, 20);
            Assert.That(body.position.x - atWall.x, Is.GreaterThan(1f));
            AssertInsideDeck();
            float wallY = body.position.y;
            Step(Vector2.down, 10);
            Assert.That(body.position.y, Is.LessThan(wallY - 0.5f));
        }

        [Test]
        public void BoatAndCameraStayFixed()
        {
            Transform cameraTransform = Camera.main.transform;
            Vector3 cameraPosition = cameraTransform.position;
            Quaternion cameraRotation = cameraTransform.rotation;
            Vector3 boatPosition = deck.transform.position;
            Quaternion boatRotation = deck.transform.rotation;
            Step(Vector2.one, 100);
            Assert.That(cameraTransform.position, Is.EqualTo(cameraPosition));
            Assert.That(cameraTransform.rotation, Is.EqualTo(cameraRotation));
            Assert.That(deck.transform.position, Is.EqualTo(boatPosition));
            Assert.That(deck.transform.rotation, Is.EqualTo(boatRotation));
        }

        [Test]
        public void BoundaryOutlineMatchesActualCollider()
        {
            var edge = deck.GetComponent<EdgeCollider2D>();
            var outline = deck.GetComponent<LineRenderer>();
            Assert.That(edge.isTrigger, Is.False);
            Assert.That(edge.points.Length, Is.EqualTo(5));
            Assert.That(edge.points[0], Is.EqualTo(edge.points[4]));
            Assert.That(outline.loop, Is.True);
            Assert.That(outline.useWorldSpace, Is.False);
            Assert.That(outline.positionCount, Is.EqualTo(4));
            for (int i = 0; i < 4; i++)
                Assert.That((Vector2)outline.GetPosition(i), Is.EqualTo(edge.points[i]));
        }

        private void Step(Vector2 direction, int count)
        {
            for (int i = 0; i < count; i++)
            {
                player.Move(direction);
                Assert.That(Physics2D.Simulate(0.02f), Is.True);
            }
        }

        private void AssertInsideDeck()
        {
            Bounds boundary = deck.GetComponent<EdgeCollider2D>().bounds;
            Bounds footprint = playerCollider.bounds;
            const float tolerance = 0.02f;
            Assert.That(footprint.min.x, Is.GreaterThanOrEqualTo(boundary.min.x - tolerance));
            Assert.That(footprint.max.x, Is.LessThanOrEqualTo(boundary.max.x + tolerance));
            Assert.That(footprint.min.y, Is.GreaterThanOrEqualTo(boundary.min.y - tolerance));
            Assert.That(footprint.max.y, Is.LessThanOrEqualTo(boundary.max.y + tolerance));
        }
    }
}
