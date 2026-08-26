using UnityEngine;

namespace RedHollow.Game.View
{
    /// <summary>
    /// 2.5D unit presentation: an upright camera-facing quad or sprite plus a ground blob
    /// shadow. Heroes and monsters stay 2D tokens standing in the 3D cavern — not floor
    /// decals, not sculpted meshes.
    /// </summary>
    public static class UnitBillboard
    {
        public const float HeroWidth = 6.4f;
        public const float HeroHeight = 7.8f;
        public const float MonsterWidth = 5.2f;
        public const float MonsterHeight = 6.2f;

        /// <summary>Placeholder quad + shadow for a hero or monster class.</summary>
        public static GameObject CreatePlaceholder(VisualClass visualClass)
        {
            var isHero = visualClass == VisualClass.Hero;
            var width = isHero ? HeroWidth : MonsterWidth;
            var height = isHero ? HeroHeight : MonsterHeight;
            var tint = isHero
                ? new Color(0.98f, 0.78f, 0.32f)
                : new Color(0.55f, 0.95f, 0.28f);

            var root = new GameObject("placeholder_" + visualClass.ToString().ToLowerInvariant());
            var quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
            quad.name = "billboard";
            quad.transform.SetParent(root.transform, false);
            quad.transform.localPosition = new Vector3(0f, height * 0.5f, 0f);
            quad.transform.localScale = new Vector3(width, height, 1f);
            ViewLook.StripCollider(quad);
            ViewLook.Paint(quad, ViewLook.Unlit(tint));
            quad.AddComponent<BillboardFacing>();

            AttachBlobShadow(root.transform, width * 0.72f);
            return root;
        }

        /// <summary>
        /// Wrap a standing sprite (already created, pivot-centred) as a camera-facing unit
        /// with a blob shadow. The returned root is what the visual handle should own.
        /// </summary>
        public static GameObject WrapStandingSprite(GameObject spriteGo, float across)
        {
            var root = new GameObject(spriteGo.name);
            spriteGo.name = "billboard";
            spriteGo.transform.SetParent(root.transform, false);
            spriteGo.transform.localRotation = Quaternion.identity;
            spriteGo.transform.localPosition = new Vector3(0f, across * 0.5f, 0f);
            if (spriteGo.GetComponent<BillboardFacing>() == null)
            {
                spriteGo.AddComponent<BillboardFacing>();
            }

            AttachBlobShadow(root.transform, Mathf.Max(1.6f, across * 0.55f));
            return root;
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
            shadow.transform.localScale = new Vector3(radius, 0.02f, radius);
            ViewLook.StripCollider(shadow);
            ViewLook.Paint(shadow, ViewLook.Unlit(new Color(0.04f, 0.02f, 0.01f, 0.85f)));
        }
    }
}
