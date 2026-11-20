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
            foreach (var enemy in Object.FindObjectsOfType<EnemyCharacter>())
                enemy.gameObject.SetActive(false);
            foreach (var obstacle in Object.FindObjectsOfType<DestructibleObstacle>())
                obstacle.gameObject.SetActive(false);
            foreach (var projectile in Object.FindObjectsOfType<Ferryman.Combat.Projectile>())
            {
                projectile.gameObject.SetActive(false);
                Object.Destroy(projectile.gameObject);
            }
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
        public void MovementHasExpectedSpeedStopsAndLimitsDiagonalInput()
        {
            Vector2 start = body.position;
            Step(Vector2.right, 10);
            Assert.That(body.position.x - start.x, Is.EqualTo(0.8f).Within(0.001f));
            Vector2 stopped = body.position;
            Step(Vector2.zero, 10);
            Assert.That(Vector2.Distance(body.position, stopped), Is.LessThan(0.001f));

            body.position = start;
            Step(Vector2.one, 10);
            Assert.That(Vector2.Distance(body.position, start), Is.EqualTo(0.8f).Within(0.001f));
        }

        [Test]
        public void PlayerColliderStaysInsideAllEdgesAndCorners()
        {
            Vector2 start = body.position;
            Bounds boundary = deck.GetComponent<EdgeCollider2D>().bounds;
            for (int x = -1; x <= 1; x++)
            for (int y = -1; y <= 1; y++)
            {
                if (x == 0 && y == 0)
                    continue;
                body.position = start;
                for (int i = 0; i < 250; i++)
                {
                    Step(new Vector2(x, y), 1);
                    AssertInsideDeck();
                }

                if (x != 0)
                    Assert.That(Mathf.Abs(body.position.x), Is.GreaterThan(boundary.extents.x - playerCollider.bounds.extents.x - 0.1f));
                if (y != 0)
                    Assert.That(Mathf.Abs(body.position.y), Is.GreaterThan(boundary.extents.y - playerCollider.bounds.extents.y - 0.1f));
            }
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
