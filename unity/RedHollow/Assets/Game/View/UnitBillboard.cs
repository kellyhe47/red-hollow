using UnityEngine;

namespace RedHollow.Game.View
{
    /// <summary>
    /// World-facing 3D unit figure — torso, head, hat, coat with thickness, arms.
    /// Yaw follows the owner (aim / walk), never the camera. Lit, not emissive,
    /// not Unlit: lanterns shade the planes. A painted card is not a figure.
    /// Presentation only.
    /// </summary>
    public static class UnitBillboard
    {
        // Street-scale PERSON, not a tan fridge: ~1.8–2.2m tall, ~0.6–0.8m across.
        // Hat brim is a dark disk so the 55° follow-cam reads a silhouette, not a lump.
        public const float HeroWidth = 0.72f;
        public const float HeroHeight = 1.95f;
        public const float HeroDepth = 0.38f;
        public const float MonsterWidth = 1.28f;
        public const float MonsterHeight = 2.55f;
        public const float MonsterDepth = 1.02f;

        /// <summary>Placeholder volume for a hero or monster class.</summary>
        public static GameObject CreatePlaceholder(VisualClass visualClass)
        {
            var isHero = visualClass == VisualClass.Hero;
            var width = isHero ? HeroWidth : MonsterWidth;
            var height = isHero ? HeroHeight : MonsterHeight;
            var depth = isHero ? HeroDepth : MonsterDepth;
            var tint = isHero
                ? new Color(0.48f, 0.28f, 0.12f)
                : new Color(0.32f, 0.40f, 0.16f);
            return CreateFigure(
                "placeholder_" + visualClass.ToString().ToLowerInvariant(),
                height, width, depth, null, tint, cowboyHat: isHero);
        }

        /// <summary>
        /// Constructed 3D figure. <paramref name="albedo"/> is stamped as a world-yawed
        /// front decal on torso/face (never camera-billboarded, never wrapping the mesh).
        /// Solid Lit materials catch lanterns.
        /// </summary>
        public static GameObject CreateFromCanon(
            string name, Texture2D albedo, string artKey, float height)
        {
            var isHero = artKey == "gunslinger" || artKey == "rancher" || artKey == "sawbones";
            var cowboyHat = artKey == "gunslinger" || artKey == "rancher";
            var behemoth = artKey == "bull_behemoth" || artKey == "BullBehemoth";
            var width = isHero ? HeroWidth : (behemoth ? 2.4f : MonsterWidth);
            var depth = isHero ? HeroDepth : (behemoth ? 1.70f : MonsterDepth);
            var tint = BodyTint(artKey);
            return CreateFigure(name, height, width, depth, albedo, tint, cowboyHat);
        }

        /// <summary>
        /// Legacy wrapper: keep the sprite as a child, but do NOT yaw it at the camera.
        /// </summary>
        public static GameObject WrapStandingSprite(GameObject spriteGo, float across)
        {
            var height = Mathf.Max(HeroHeight, across);
            var root = CreateFigure(
                spriteGo.name, height, HeroWidth, HeroDepth, null,
                new Color(0.42f, 0.24f, 0.12f), cowboyHat: false);
            spriteGo.name = "billboard";
            spriteGo.transform.SetParent(root.transform, false);
            spriteGo.transform.localRotation = Quaternion.identity;
            spriteGo.transform.localPosition = new Vector3(0f, height * 0.5f, HeroDepth * 0.52f);
            PromoteReadOrder(spriteGo.GetComponent<Renderer>());
            var facing = spriteGo.GetComponent<BillboardFacing>();
            if (facing != null)
            {
                Object.DestroyImmediate(facing);
            }

            return root;
        }

        public static GameObject CreateFigure(
            string name, float height, float width, float depth,
            Texture albedo, Color bodyColor, bool cowboyHat)
        {
            if (height < 0.5f)
            {
                height = HeroHeight;
            }

            var root = new GameObject(name);

            var pick = root.AddComponent<CapsuleCollider>();
            pick.isTrigger = true;
            pick.radius = Mathf.Max(0.28f, width * 0.42f);
            pick.height = height;
            pick.center = new Vector3(0f, height * 0.5f, 0f);

            if (cowboyHat)
            {
                BuildGunslinger(root.transform, height, width, depth, albedo);
            }
            else
            {
                BuildCreature(root.transform, height, width, depth, bodyColor);
            }

            AttachBlobShadow(root.transform, Mathf.Max(0.40f, width * 0.78f));
            return root;
        }

        /// <summary>
        /// Person-scale gunslinger from the 55° follow-cam: dark hat disk, thin duster,
        /// visible arms/legs/boots, cream shirt, skin head, rifle along +Z in the right hand.
        /// High-contrast Lit materials — not one tan appliance.
        /// </summary>
        private static void BuildGunslinger(
            Transform root, float height, float width, float depth, Texture albedo)
        {
            var coat = ViewLook.Lit(new Color(0.14f, 0.08f, 0.05f), smoothness: 0.16f, emit: false);
            var coatDark = ViewLook.Lit(new Color(0.10f, 0.06f, 0.04f), smoothness: 0.12f, emit: false);
            var hat = ViewLook.Lit(new Color(0.16f, 0.09f, 0.05f), smoothness: 0.10f, emit: false);
            var shirt = ViewLook.Lit(new Color(0.93f, 0.87f, 0.74f), smoothness: 0.14f, emit: false);
            var skin = ViewLook.Lit(new Color(0.82f, 0.62f, 0.48f), smoothness: 0.20f, emit: false);
            var pants = ViewLook.Lit(new Color(0.16f, 0.22f, 0.36f), smoothness: 0.10f, emit: false);
            var boots = ViewLook.Lit(new Color(0.10f, 0.07f, 0.04f), smoothness: 0.18f, emit: false);
            var gloves = ViewLook.Lit(new Color(0.12f, 0.08f, 0.05f), smoothness: 0.14f, emit: false);
            var belt = ViewLook.Lit(new Color(0.22f, 0.12f, 0.07f), smoothness: 0.22f, emit: false);
            var wood = ViewLook.Lit(new Color(0.28f, 0.16f, 0.09f), smoothness: 0.22f, emit: false);
            var iron = ViewLook.Lit(new Color(0.38f, 0.36f, 0.34f), smoothness: 0.38f, emit: false);

            var hipY = height * 0.48f;
            var chestY = height * 0.62f;
            var shoulderY = height * 0.72f;
            var neckY = height * 0.80f;
            var headY = height * 0.86f;
            var brimY = height * 0.91f;
            var crownY = height * 0.97f;

            var stance = 0.11f;
            var legR = 0.085f;
            var torsoW = Mathf.Clamp(width * 0.42f, 0.28f, 0.36f);
            var torsoD = Mathf.Clamp(depth * 0.52f, 0.16f, 0.22f);
            const float coatT = 0.045f;

            // Legs + boots — readable under the hem from 55°.
            var legHalf = hipY * 0.46f;
            Part(root, "leg_L", PrimitiveType.Cylinder,
                new Vector3(-stance, hipY * 0.50f, 0.02f),
                new Vector3(legR * 2f, legHalf, legR * 2f),
                Quaternion.identity, pants);
            Part(root, "leg_R", PrimitiveType.Cylinder,
                new Vector3(stance, hipY * 0.50f, 0.02f),
                new Vector3(legR * 2f, legHalf, legR * 2f),
                Quaternion.identity, pants);
            Part(root, "boot_L", PrimitiveType.Cube,
                new Vector3(-stance, 0.07f, 0.05f),
                new Vector3(0.13f, 0.12f, 0.22f),
                Quaternion.identity, boots);
            Part(root, "boot_R", PrimitiveType.Cube,
                new Vector3(stance, 0.07f, 0.05f),
                new Vector3(0.13f, 0.12f, 0.22f),
                Quaternion.identity, boots);

            // Cream shirt — the chest read, not buried in a wrapping cube.
            Part(root, "torso", PrimitiveType.Cube,
                new Vector3(0f, chestY, 0.02f),
                new Vector3(torsoW, height * 0.22f, torsoD),
                Quaternion.identity, shirt);

            // Thin duster: open front flaps + back panel. Thickness is a few cm, not a fridge.
            Part(root, "duster_back", PrimitiveType.Cube,
                new Vector3(0f, chestY - 0.04f, -torsoD * 0.55f - coatT * 0.5f),
                new Vector3(torsoW + 0.10f, height * 0.36f, coatT),
                Quaternion.identity, coat);
            Part(root, "duster_L", PrimitiveType.Cube,
                new Vector3(-(torsoW * 0.42f + 0.04f), chestY - 0.06f, 0.04f),
                new Vector3(coatT + 0.02f, height * 0.34f, torsoD + 0.04f),
                Quaternion.Euler(0f, 0f, 8f), coat);
            Part(root, "duster_R", PrimitiveType.Cube,
                new Vector3(torsoW * 0.42f + 0.04f, chestY - 0.06f, 0.04f),
                new Vector3(coatT + 0.02f, height * 0.34f, torsoD + 0.04f),
                Quaternion.Euler(0f, 0f, -8f), coat);
            Part(root, "duster_hem", PrimitiveType.Cube,
                new Vector3(0f, hipY * 0.62f, -torsoD * 0.58f - coatT * 0.5f),
                new Vector3(torsoW + 0.16f, 0.10f, coatT),
                Quaternion.identity, coatDark);
            Part(root, "duster_shoulder_L", PrimitiveType.Cube,
                new Vector3(-torsoW * 0.48f, shoulderY, 0.01f),
                new Vector3(0.16f, 0.07f, torsoD + 0.04f),
                Quaternion.Euler(0f, 0f, 16f), coat);
            Part(root, "duster_shoulder_R", PrimitiveType.Cube,
                new Vector3(torsoW * 0.48f, shoulderY, 0.01f),
                new Vector3(0.16f, 0.07f, torsoD + 0.04f),
                Quaternion.Euler(0f, 0f, -16f), coat);
            Part(root, "collar_L", PrimitiveType.Cube,
                new Vector3(-0.06f, neckY - 0.02f, 0.05f),
                new Vector3(0.08f, 0.10f, 0.08f),
                Quaternion.Euler(18f, -18f, -10f), coatDark);
            Part(root, "collar_R", PrimitiveType.Cube,
                new Vector3(0.06f, neckY - 0.02f, 0.05f),
                new Vector3(0.08f, 0.10f, 0.08f),
                Quaternion.Euler(18f, 18f, 10f), coatDark);

            Part(root, "belt", PrimitiveType.Cube,
                new Vector3(0f, hipY + 0.02f, 0.03f),
                new Vector3(torsoW + 0.04f, 0.06f, torsoD + 0.03f),
                Quaternion.identity, belt);
            Part(root, "holster", PrimitiveType.Cube,
                new Vector3(0.14f, hipY - 0.06f, 0.06f),
                new Vector3(0.07f, 0.16f, 0.08f),
                Quaternion.Euler(12f, 0f, 8f), belt);

            // Arms — silhouette of a person, not a coat-box.
            Part(root, "arm_L", PrimitiveType.Cylinder,
                new Vector3(-torsoW * 0.62f - 0.05f, chestY - 0.04f, 0.03f),
                new Vector3(0.09f, height * 0.12f, 0.09f),
                Quaternion.Euler(12f, 0f, 18f), coat);
            Part(root, "glove_L", PrimitiveType.Sphere,
                new Vector3(-torsoW * 0.72f - 0.06f, chestY - height * 0.16f, 0.10f),
                Vector3.one * 0.09f,
                Quaternion.identity, gloves);
            Part(root, "arm_R", PrimitiveType.Cylinder,
                new Vector3(torsoW * 0.58f + 0.05f, chestY - 0.02f, 0.08f),
                new Vector3(0.09f, height * 0.11f, 0.09f),
                Quaternion.Euler(22f, 0f, -14f), coat);
            Part(root, "glove_R", PrimitiveType.Sphere,
                new Vector3(torsoW * 0.62f + 0.07f, chestY - height * 0.13f, 0.18f),
                Vector3.one * 0.09f,
                Quaternion.identity, gloves);

            // Rifle in the right hand, along facing (+Z). Barrel is the muzzle socket.
            Part(root, "rifle_stock", PrimitiveType.Cube,
                new Vector3(torsoW * 0.50f + 0.05f, chestY - height * 0.10f, 0.22f),
                new Vector3(0.05f, 0.08f, 0.22f),
                Quaternion.Euler(12f, -6f, 0f), wood);
            Part(root, "rifle_barrel", PrimitiveType.Cylinder,
                new Vector3(torsoW * 0.48f + 0.05f, chestY - height * 0.04f, 0.48f),
                new Vector3(0.035f, 0.28f, 0.035f),
                Quaternion.Euler(90f, 0f, 0f), iron);

            Part(root, "head", PrimitiveType.Sphere,
                new Vector3(0f, headY, 0.03f),
                Vector3.one * 0.22f,
                Quaternion.identity, skin);

            // Dark hat brim + crown — THE read from 55°. Disk, not a sliver, not a tan lid.
            var brimD = 0.78f;
            Part(root, "hat_brim", PrimitiveType.Cylinder,
                new Vector3(0f, brimY, 0.02f),
                new Vector3(brimD, 0.025f, brimD),
                Quaternion.identity, hat);
            Part(root, "hat_crown", PrimitiveType.Cube,
                new Vector3(0f, crownY, 0.02f),
                new Vector3(0.28f, 0.14f, 0.32f),
                Quaternion.identity, hat);

            StampFrontDecal(root, albedo, height, torsoW, chestY, headY, torsoD);
        }

        /// <summary>
        /// Canon albedo as a WORLD-YAWED front-plane decal on torso/face. Parent yaw (aim)
        /// turns it; it never faces the camera.
        /// </summary>
        private static void StampFrontDecal(
            Transform root, Texture albedo, float height, float torsoW,
            float chestY, float headY, float torsoD)
        {
            if (albedo == null)
            {
                return;
            }

            var card = GameObject.CreatePrimitive(PrimitiveType.Quad);
            card.name = "canon_decal";
            card.transform.SetParent(root, false);
            var cardH = height * 0.40f;
            var cardW = Mathf.Clamp(torsoW * 1.15f, 0.30f, 0.46f);
            card.transform.localPosition = new Vector3(
                0f, (chestY + headY) * 0.50f, torsoD * 0.55f + 0.03f);
            card.transform.localRotation = Quaternion.identity;
            card.transform.localScale = new Vector3(cardW, cardH, 1f);
            ViewLook.StripCollider(card);
            var mat = ViewLook.LitCutout(Color.white, albedo, emit: false);
            ViewLook.Paint(card, mat, castShadows: false);
        }

        /// <summary>
        /// Cheap matching volume for shamblers and other non-cowboy units: hunched
        /// torso, head, two arms. Lit, not a card.
        /// </summary>
        private static void BuildCreature(
            Transform root, float height, float width, float depth, Color bodyColor)
        {
            var hide = ViewLook.Lit(bodyColor, smoothness: 0.08f, emit: false);
            var hideDark = ViewLook.Lit(
                new Color(bodyColor.r * 0.65f, bodyColor.g * 0.65f, bodyColor.b * 0.55f),
                smoothness: 0.07f, emit: false);
            var claw = ViewLook.Lit(
                new Color(bodyColor.r * 0.55f, bodyColor.g * 0.50f, bodyColor.b * 0.40f),
                smoothness: 0.12f, emit: false);

            var hunched = Quaternion.Euler(18f, 0f, 0f);
            Part(root, "body", PrimitiveType.Capsule,
                new Vector3(0f, height * 0.38f, 0.08f),
                new Vector3(width * 0.78f, height * 0.32f, depth * 0.85f),
                hunched, hide);
            Part(root, "head", PrimitiveType.Sphere,
                new Vector3(0f, height * 0.72f, depth * 0.22f),
                Vector3.one * Mathf.Clamp(width * 0.52f, 0.38f, 0.70f),
                Quaternion.identity, hideDark);
            Part(root, "arm_L", PrimitiveType.Cylinder,
                new Vector3(-width * 0.42f, height * 0.40f, 0.10f),
                new Vector3(0.16f, height * 0.18f, 0.16f),
                Quaternion.Euler(25f, 0f, 22f), hide);
            Part(root, "arm_R", PrimitiveType.Cylinder,
                new Vector3(width * 0.42f, height * 0.40f, 0.10f),
                new Vector3(0.16f, height * 0.18f, 0.16f),
                Quaternion.Euler(25f, 0f, -22f), hide);
            Part(root, "claw_L", PrimitiveType.Sphere,
                new Vector3(-width * 0.52f, height * 0.18f, 0.22f),
                Vector3.one * 0.18f,
                Quaternion.identity, claw);
            Part(root, "claw_R", PrimitiveType.Sphere,
                new Vector3(width * 0.52f, height * 0.18f, 0.22f),
                Vector3.one * 0.18f,
                Quaternion.identity, claw);
        }

        private static GameObject Part(
            Transform parent, string name, PrimitiveType type,
            Vector3 localPos, Vector3 localScale, Quaternion localRot, Material material)
        {
            var go = GameObject.CreatePrimitive(type);
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            go.transform.localRotation = localRot;
            go.transform.localScale = localScale;
            ViewLook.StripCollider(go);
            ViewLook.Paint(go, material, castShadows: true);
            return go;
        }

        public static void PromoteReadOrder(Renderer renderer)
        {
            if (renderer == null)
            {
                return;
            }

            renderer.sortingOrder = 40;
            var sprite = renderer as SpriteRenderer;
            if (sprite != null)
            {
                sprite.sortingOrder = 40;
            }
        }

        public static void AttachBlobShadow(Transform owner, float radius)
        {
            if (owner == null)
            {
                return;
            }

            var shadow = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            shadow.name = "blob_shadow";
            shadow.transform.SetParent(owner, false);
            shadow.transform.localPosition = new Vector3(0f, 0.03f, 0f);
            shadow.transform.localScale = new Vector3(radius, 0.02f, radius * 0.78f);
            ViewLook.StripCollider(shadow);
            ViewLook.Paint(shadow, ViewLook.Unlit(new Color(0.04f, 0.02f, 0.01f, 0.85f)));
        }

        private static Color BodyTint(string artKey)
        {
            if (artKey == "gunslinger" || artKey == "rancher")
            {
                return new Color(0.42f, 0.24f, 0.12f);
            }

            if (artKey == "sawbones")
            {
                return new Color(0.50f, 0.48f, 0.40f);
            }

            if (artKey == "spitter")
            {
                return new Color(0.28f, 0.42f, 0.16f);
            }

            if (artKey == "ravager")
            {
                return new Color(0.36f, 0.22f, 0.12f);
            }

            if (artKey == "burrower")
            {
                return new Color(0.38f, 0.26f, 0.14f);
            }

            if (artKey == "bull_behemoth" || artKey == "BullBehemoth")
            {
                return new Color(0.34f, 0.20f, 0.10f);
            }

            return new Color(0.30f, 0.36f, 0.16f);
        }
    }
}
