#!/usr/bin/env python3
"""
Passage de bout en bout : commande prête -> course -> offre -> acceptation -> livraison
-> coursier payé.

CE QUE CE SCRIPT VÉRIFIE, ET QUE RIEN D'AUTRE NE VÉRIFIE.

Les tests unitaires et d'intégration couvrent chaque morceau. Aucun ne couvre les
JOINTURES : les deux services se compilent séparément, ne partagent aucun type, et
n'échangent que du JSON signé. C'est exactement là que ce genre de système casse — un
champ renommé, un identifiant qui n'est pas celui qu'on croit, un secret croisé à
l'envers. Rien de tout cela n'apparaît à la compilation.

PRÉREQUIS
  - Hôte API           http://localhost:5080   (draine l'outbox : OUTBOX_ENABLED non posé)
  - BFF Admin          http://localhost:5003
  - BFF Client         http://localhost:5000
  - BFF Driveur        http://localhost:5004
  - Service dispatch   http://localhost:5210
  - Un administrateur d'amorçage (Identity:Bootstrap:AdminEmail / AdminPassword)

USAGE
  ADMIN_EMAIL=admin@hba.local ADMIN_PASSWORD='…' python3 scripts/e2e_dispatch.py

Aucune dépendance : bibliothèque standard uniquement.
"""

import json
import os
import sys
import time
import urllib.error
import urllib.request
import uuid

API = os.environ.get("API", "http://localhost:5080")
ADMIN_BFF = os.environ.get("ADMIN_BFF", "http://localhost:5003")
CLIENT_BFF = os.environ.get("CLIENT_BFF", "http://localhost:5000")
DRIVER_BFF = os.environ.get("DRIVER_BFF", "http://localhost:5004")
DISPATCH = os.environ.get("DISPATCH", "http://localhost:5210")

ADMIN_EMAIL = os.environ.get("ADMIN_EMAIL")
ADMIN_PASSWORD = os.environ.get("ADMIN_PASSWORD")

COMMUNE = os.environ.get("COMMUNE", "cotonou")
PASSWORD = os.environ.get("TEST_PASSWORD", "Passw0rd!2026")

# Ramassage et coursier à quelques centaines de mètres : la première vague d'offres a un
# rayon de 3 km, et un coursier hors rayon ne serait jamais appelé.
PICKUP_LAT, PICKUP_LON = 6.3700, 2.3900
COURIER_LAT, COURIER_LON = 6.3720, 2.3930
DROPOFF_LAT, DROPOFF_LON = 6.3800, 2.4000

RUN = uuid.uuid4().hex[:8]

# ── Sortie ───────────────────────────────────────────────────────────────────

_step = 0


def step(label):
    global _step
    _step += 1
    print(f"\n\033[1m[{_step:02d}] {label}\033[0m", flush=True)


def info(msg):
    print(f"     {msg}", flush=True)


def ok(msg):
    print(f"     \033[32m✓\033[0m {msg}", flush=True)


def die(msg, detail=None):
    print(f"\n\033[31m✗ ÉCHEC — {msg}\033[0m", flush=True)
    if detail:
        print(f"\n{detail}", flush=True)
    sys.exit(1)


# ── HTTP ─────────────────────────────────────────────────────────────────────


def http(method, base, path, token=None, body=None, expect=(200, 201, 202, 204), quiet=False):
    url = base + path
    data = json.dumps(body).encode() if body is not None else None
    req = urllib.request.Request(url, data=data, method=method)
    req.add_header("Accept", "application/json")
    if data is not None:
        req.add_header("Content-Type", "application/json")
    if token:
        req.add_header("Authorization", "Bearer " + token)

    try:
        with urllib.request.urlopen(req, timeout=30) as resp:
            raw = resp.read().decode() or ""
            status = resp.status
    except urllib.error.HTTPError as e:
        raw = e.read().decode() or ""
        status = e.code
    except urllib.error.URLError as e:
        die(f"{method} {url} injoignable", f"{e.reason}\n\nL'hôte est-il démarré ?")

    parsed = None
    if raw.strip():
        try:
            parsed = json.loads(raw)
        except json.JSONDecodeError:
            parsed = raw

    if expect and status not in expect:
        die(
            f"{method} {url} a répondu {status} (attendu {expect})",
            json.dumps(parsed, indent=2, ensure_ascii=False) if isinstance(parsed, (dict, list)) else str(parsed),
        )

    if not quiet:
        info(f"{method} {path} → {status}")

    return status, parsed


def pick(obj, *names):
    """Récupère une valeur quel que soit la casse du champ (camelCase / PascalCase)."""
    if not isinstance(obj, dict):
        return None
    lowered = {k.lower(): v for k, v in obj.items()}
    for name in names:
        if name.lower() in lowered:
            return lowered[name.lower()]
    return None


def poll(what, fn, timeout=90, every=3):
    """Attend qu'une condition asynchrone se produise (outbox, drain, dispatch)."""
    info(f"attente : {what} (jusqu'à {timeout} s)")
    deadline = time.time() + timeout
    while time.time() < deadline:
        value = fn()
        if value is not None:
            return value
        time.sleep(every)
    die(
        f"délai dépassé : {what}",
        "Cause la plus probable : l'outbox ne draine pas.\n"
        "Vérifier que l'hôte API tourne SANS OUTBOX_ENABLED=false, et lire ses journaux.",
    )


# ═════════════════════════════════════════════════════════════════════════════
# LE PARCOURS
# ═════════════════════════════════════════════════════════════════════════════


def main():
    if not ADMIN_EMAIL or not ADMIN_PASSWORD:
        die(
            "ADMIN_EMAIL et ADMIN_PASSWORD sont obligatoires",
            "Ce sont les identifiants de l'administrateur d'amorçage\n"
            "(Identity:Bootstrap:AdminEmail / AdminPassword).",
        )

    print(f"\033[1mPassage de bout en bout — exécution {RUN}\033[0m")
    print(f"  API {API} · Admin {ADMIN_BFF} · Client {CLIENT_BFF} · "
          f"Driveur {DRIVER_BFF} · Dispatch {DISPATCH}")

    # ── 1. Administrateur ────────────────────────────────────────────────────
    step("Connexion administrateur")
    admin_token = login(ADMIN_EMAIL, ADMIN_PASSWORD)
    ok("jeton administrateur obtenu")

    # ── 2. Comptes acheteur et coursier ──────────────────────────────────────
    step("Création de l'acheteur et du coursier")
    buyer_id, buyer_email = create_user(admin_token, "Acheteur", "Test")
    courier_user_id, courier_email = create_user(admin_token, "Kossi", "Adjovi")
    ok(f"acheteur {buyer_id}")
    ok(f"compte coursier {courier_user_id}")

    buyer_token = login(buyer_email, PASSWORD)
    courier_token = login(courier_email, PASSWORD)
    ok("les deux se connectent")

    # ── 3. Vendeur (l'administrateur joue ce rôle) ───────────────────────────
    step("Vendeur : onboarding, versement, KYB, activation")
    _, r = http("POST", API, "/api/sellers/", admin_token, {"shopName": f"Boutique {RUN}"})
    seller_id = pick(r, "id")
    if not seller_id:
        die("identifiant vendeur absent de la réponse", json.dumps(r, indent=2, ensure_ascii=False))

    http("PUT", API, "/api/sellers/me/payout", admin_token,
         {"provider": "MtnMomo", "accountNumber": "0197000001", "accountName": f"Boutique {RUN}"})
    http("POST", API, "/api/sellers/me/kyb", admin_token,
         {"type": "Ifu", "fileUrl": "https://example.invalid/kyb.pdf"}, expect=None)
    http("POST", API, f"/api/sellers/admin/{seller_id}/kyb/approve", admin_token, expect=None)
    http("POST", API, f"/api/sellers/admin/{seller_id}/activate", admin_token, expect=None)
    ok(f"vendeur {seller_id}")

    # ── 4. Lieu de ramassage ─────────────────────────────────────────────────
    step("Lieu d'expédition (c'est le point de ramassage du coursier)")
    _, r = http("POST", API, "/api/inventory/locations/", admin_token, {
        "type": "SellerAddress", "ownerId": seller_id,
        "commune": COMMUNE, "quartier": "Gbedjromede", "landmark": "Face pharmacie Zogbo",
        "line": "Rue 12.045", "latitude": PICKUP_LAT, "longitude": PICKUP_LON,
    })
    location_id = pick(r, "id")
    ok(f"lieu {location_id} — {COMMUNE} ({PICKUP_LAT}, {PICKUP_LON})")

    # ── 5. Catalogue et offre ────────────────────────────────────────────────
    step("Catégorie, produit, offre, stock")
    _, r = http("POST", API, "/api/catalog/categories/", admin_token, {"name": f"Categorie {RUN}"})
    category_id = pick(r, "id")
    http("POST", API, f"/api/catalog/categories/{category_id}/publish", admin_token, expect=None)

    _, r = http("POST", API, "/api/catalog/products/", admin_token, {
        "sellerId": seller_id, "categoryId": category_id,
        "name": f"Produit {RUN}", "description": "Article de recette pour le passage de bout en bout.",
    })
    product_id = pick(r, "id")
    http("PATCH", API, f"/api/catalog/products/{product_id}/status", admin_token, {"status": "Active"}, expect=None)

    sku = f"SKU-{RUN}"
    _, r = http("POST", API, "/api/offers/", admin_token, {
        "productId": product_id, "sellerId": seller_id, "sku": sku,
        "sellerPrice": 10000, "currency": "XOF", "condition": "New",
        "fulfillmentType": "Fbs", "shipFromLocationId": location_id, "handlingTime": 1,
    })
    offer_id = pick(r, "id")

    http("POST", API, "/api/inventory/items/", admin_token,
         {"sku": sku, "locationId": location_id, "onHand": 10, "reorderThreshold": 1})
    ok(f"offre {offer_id} — {sku}, 10 000 XOF, 10 en stock")

    # ── 6. Coursier : enregistrement et activation ───────────────────────────
    # AVANT la commande, délibérément : la première vague d'offres part à la seconde où
    # la commande est déclarée prête. Un coursier mis en ligne après ne serait pas appelé.
    step("Coursier : enregistrement, activation, mise en ligne")
    _, r = http("POST", ADMIN_BFF, "/admin/couriers", admin_token, {
        "userId": courier_user_id, "displayName": "Kossi Adjovi", "phone": "0197000002",
        "payoutProvider": "MtnMomo", "payoutAccountNumber": "0197000002", "payoutAccountName": "Kossi Adjovi",
    })
    courier_id = pick(r, "id")
    http("POST", ADMIN_BFF, f"/admin/couriers/{courier_id}/activate", admin_token, expect=None)
    ok(f"coursier {courier_id} (compte {courier_user_id})")

    # LE PREMIER VRAI POINT DE JONCTION : la marketplace doit avoir annoncé ce coursier
    # au service de dispatch, par l'outbox. Tant que ce n'est pas fait, il y est inconnu.
    def courier_known():
        status, _ = http("PUT", DISPATCH, "/api/courier/service-area", courier_token,
                         {"communeCodes": [COMMUNE]}, expect=None, quiet=True)
        return True if status in (200, 204) else None

    poll("le dispatch connaît le coursier (outbox marketplace → dispatch)", courier_known)
    ok("zone de service enregistrée — le coursier existe côté dispatch")

    http("PUT", DISPATCH, "/api/courier/position", courier_token,
         {"latitude": COURIER_LAT, "longitude": COURIER_LON})
    http("POST", DISPATCH, "/api/courier/online", courier_token)
    ok(f"en ligne, position ({COURIER_LAT}, {COURIER_LON})")

    # ── 7. Commande ──────────────────────────────────────────────────────────
    step("Panier, adresse, commande, paiement")
    http("POST", API, "/api/cart/items", buyer_token, {"offerId": offer_id, "quantity": 1})

    _, r = http("POST", CLIENT_BFF, "/client/checkout/addresses", buyer_token, {
        "label": "Domicile", "recipient": "Acheteur Test", "phone": "0197000003",
        "commune": COMMUNE, "quartier": "Fidjrosse", "landmark": "Après le carrefour Fidjrosse",
        "line1": "Rue 8.112", "latitude": DROPOFF_LAT, "longitude": DROPOFF_LON, "makeDefault": True,
    })
    address_id = pick(r, "id", "addressId")

    _, r = http("POST", CLIENT_BFF, "/client/checkout/pay", buyer_token, {
        "method": "MobileMoney", "provider": "MtnMomo", "flow": "HostedCheckout",
        "payerPhone": "0197000003", "addressId": address_id, "shippingCode": "standard",
    })
    order_id = pick(r, "orderId")
    payment = pick(r, "payment")
    payment_id = pick(payment, "id", "paymentId") if isinstance(payment, dict) else None
    ok(f"commande {order_id}")

    if payment_id:
        # Passerelle simulée : le retour de redirection rend toujours « Captured ».
        http("GET", API, f"/api/payments/return/{payment_id}")
        ok("paiement capturé (passerelle simulée)")
    else:
        info("aucun paiement à confirmer (commande déjà couverte)")

    # ── 8. Expéditions ───────────────────────────────────────────────────────
    # Elles n'existent pas avant la confirmation : elles sont créées par un gestionnaire
    # d'événement d'intégration, donc APRÈS un tour d'outbox.
    step("Expéditions et mise en préparation")

    def shipments_ready():
        _, body = http("GET", API, f"/api/shipments/order/{order_id}", admin_token,
                       expect=None, quiet=True)
        return body if isinstance(body, list) and body else None

    shipments = poll("création des expéditions (outbox : commande confirmée)", shipments_ready)
    ok(f"{len(shipments)} expédition(s)")

    for shipment in shipments:
        shipment_id = pick(shipment, "id", "shipmentId")
        http("POST", API, f"/api/shipments/{shipment_id}/prepare", admin_token)
    ok("toutes les expéditions en préparation — « commande prête à livrer » est publié")

    # ── 9. La course arrive chez le coursier ─────────────────────────────────
    # LE POINT DE JONCTION QUI COMPTE : l'événement traverse l'outbox, le canal HTTP
    # signé, la création de course et la première vague d'offres.
    step("La course parvient au coursier")

    def offer_visible():
        _, body = http("GET", DISPATCH, "/api/courier/offers", courier_token, expect=None, quiet=True)
        return body[0] if isinstance(body, list) and body else None

    offer = poll("offre visible côté coursier (marketplace → dispatch → offre)", offer_visible)
    job_id = pick(offer, "jobId")
    pickups = pick(offer, "pickups") or []
    ok(f"course {job_id} offerte — {len(pickups)} ramassage(s), "
       f"{pick(offer, 'shippingFee')} {pick(offer, 'currency')}")

    # ── 10. Acceptation et progression ───────────────────────────────────────
    #
    # LA POSITION EST RENVOYÉE AVANT CHAQUE ACTION, comme le ferait la vraie application.
    # Accepter, ramasser, partir et livrer exigent une position de MOINS DE CINQ MINUTES
    # (CourierActionPolicy) : c'est ainsi qu'est traduite la règle « le coursier ne fait
    # pas une course depuis le web ». Les attentes ci-dessus peuvent durer plus longtemps
    # que ça — sans ce rafraîchissement, le script échouerait sur une règle qui fait
    # exactement son travail.
    step("Acceptation, ramassages, transit, livraison")

    def keep_position():
        http("PUT", DISPATCH, "/api/courier/position", courier_token,
             {"latitude": COURIER_LAT, "longitude": COURIER_LON}, quiet=True)

    keep_position()
    http("POST", DISPATCH, f"/api/courier/jobs/{job_id}/accept", courier_token)
    ok("course acceptée")

    keep_position()
    http("POST", DISPATCH, f"/api/courier/jobs/{job_id}/pickup", courier_token)
    for pickup in pickups:
        pickup_id = pick(pickup, "pickupPointId")
        keep_position()
        http("POST", DISPATCH, f"/api/courier/jobs/{job_id}/pickups/{pickup_id}/collected", courier_token)
    ok("colis ramassés")

    keep_position()
    http("POST", DISPATCH, f"/api/courier/jobs/{job_id}/transit", courier_token)
    keep_position()
    http("POST", DISPATCH, f"/api/courier/jobs/{job_id}/delivered", courier_token)
    ok("livrée — la rémunération est en file côté dispatch")

    # ── 11. Le coursier est payé ─────────────────────────────────────────────
    # LE DERNIER POINT DE JONCTION, ET LE SEUL QUI COÛTE DE L'ARGENT : la file sortante
    # du dispatch, la signature HMAC, le plafond côté marketplace, le grand livre.
    step("Le portefeuille du coursier est crédité")

    def wallet_credited():
        # LE PORTEFEUILLE A CHANGÉ D'HÔTE. Il vivait sur /mobile/courier/wallet ; depuis
        # le découpage par audience, il est servi par le BFF Driveur sur /driver/wallet —
        # la même route pour l'application mobile du coursier et pour sa console web.
        _, body = http("GET", DRIVER_BFF, "/driver/wallet", courier_token,
                       expect=None, quiet=True)
        if not isinstance(body, dict):
            return None
        available = pick(body, "available", "availableBalance", "balance")
        try:
            return body if available is not None and float(available) > 0 else None
        except (TypeError, ValueError):
            return None

    wallet = poll("crédit du portefeuille (drain sortant dispatch → marketplace)", wallet_credited)
    ok(f"portefeuille crédité : {json.dumps(wallet, ensure_ascii=False)}")

    # ── 12. Le grand livre est cohérent ──────────────────────────────────────
    step("Réconciliation du grand livre")
    status, body = http("GET", ADMIN_BFF, "/admin/wallet/reconciliation", admin_token, expect=None)
    if status == 200:
        ok("200 — soldes et grand livre concordent")
    elif status == 409:
        die("409 — DÉRIVE ENTRE LES SOLDES ET LE GRAND LIVRE",
            json.dumps(body, indent=2, ensure_ascii=False))
    else:
        info(f"réconciliation indisponible ici ({status}) — essayer {API}/admin/wallet/reconciliation")

    print("\n\033[1;32mPASSAGE COMPLET.\033[0m "
          "Commande prête → course → offre → acceptation → livraison → coursier payé.")
    print(f"  commande {order_id}\n  course   {job_id}\n  coursier {courier_id}")


# ── Utilitaires de parcours ──────────────────────────────────────────────────


def login(email, password):
    _, r = http("POST", API, "/api/identity/auth/login", body={"email": email, "password": password})
    tokens = pick(r, "tokens")
    token = pick(tokens, "accessToken") if isinstance(tokens, dict) else None
    if not token:
        die(f"connexion impossible pour {email}", json.dumps(r, indent=2, ensure_ascii=False))
    return token


def create_user(admin_token, first, last):
    email = f"{first.lower()}.{RUN}@e2e.local"
    _, r = http("POST", ADMIN_BFF, "/admin/users/register", admin_token, {
        "firstName": first, "lastName": last, "email": email,
        "phoneNumber": "0197000000", "password": PASSWORD,
    })
    user_id = pick(r, "id", "userId")
    if not user_id:
        die(f"identifiant absent à la création de {email}", json.dumps(r, indent=2, ensure_ascii=False))

    # Le compte peut naître en attente de validation selon la configuration : on approuve
    # sans condition, l'appel étant sans effet si le compte est déjà actif.
    http("POST", ADMIN_BFF, f"/admin/users/{user_id}/approve", admin_token, expect=None, quiet=True)
    return user_id, email


if __name__ == "__main__":
    main()
