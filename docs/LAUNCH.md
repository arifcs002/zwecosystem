# Auleco — লঞ্চ প্রস্তুতি (Launch guide)

এই ফাইলে আছে: লঞ্চের আগে কী কী করতে হবে, কীভাবে চেক করবেন, কিছু ভাঙলে কীভাবে ফেরত যাবেন।
**নোট:** এখানকার স্ক্রিপ্টগুলো (`scripts/`) লোকাল মেশিনে `bash -n` দিয়ে সিনট্যাক্স দেখা হয়েছে এবং `smoke-test.sh` লোকাল API-তে চালিয়ে দেখা হয়েছে।
`rollback.sh`, `restore-db.sh`, `nightly-backup.sh` আর nginx-এর নতুন কনফিগ প্রোডাকশন VPS-এ **এখনো চালানো হয়নি**
(এই মেশিনে docker/nginx নেই)। প্রথমবার শান্ত সময়ে, SSH খোলা রেখে চালান।

---

## ১. লঞ্চের আগের চেকলিস্ট

### A. নিরাপত্তা
- [ ] HTTPS চালু (নিচে ৩ নম্বর অংশ)
- [ ] VPS `.env`-এ `JWT_SECRET` কমপক্ষে ৩২ অক্ষরের র‍্যান্ডম (`openssl rand -base64 48`)
- [ ] `SUPERADMIN_PASSWORD` শক্তিশালী, আর কোথাও লেখা নেই
- [ ] সব ঠিক থাকলে `.env`-এ `STARTUP_STRICT=true` দিন: তখন দুর্বল সিক্রেট থাকলে সার্ভার চালুই হবে না (এখন শুধু লগে সতর্কতা দেয়)
- [ ] পোর্ট 5500/5436/6379 ইন্টারনেটে খোলা নেই (`sudo ss -tulnp`-এ `127.0.0.1` দেখবেন)
- [ ] ডিপ্লয়ের পর ডিপ্লয় লগে `CONFIG WARNING` / `CONFIG PROBLEM` লাইন নেই: `docker logs zwecosystem-api | grep CONFIG`

### B. ব্যাকআপ
- [ ] Google Drive ব্যাকআপ চালু (নিচে ২ নম্বর অংশ)
- [ ] রাতের ব্যাকআপ cron বসানো
- [ ] **একবার রিস্টোর ড্রিল করা** (`restore-db.sh` ডিফল্ট মোড): ব্যাকআপ আসলে কাজ করে কি না প্রমাণ হয়

### C. ডিপ্লয় ও ফেরত যাওয়া
- [ ] ডিপ্লয়ের পর GitHub Actions-এর "Smoke test the live site" ধাপ সবুজ
- [ ] `rollback.sh` একবার চালিয়ে দেখা (ঘুমন্ত সময়ে), তারপর আবার ডিপ্লয়

### D. আসল সার্ভিসে যাচাই (এগুলো শুধু আপনিই করতে পারেন)
- [ ] আসল SMS gateway: একটা OTP বা ক্যাম্পেইন নিজের নম্বরে পাঠিয়ে দেখুন (Dashboard-এর "Send one now" দিয়েও সারাংশ SMS দেখা যায়)
- [ ] Steadfast: একটা টেস্ট অর্ডার বুক করে স্ট্যাটাস দেখুন
- [ ] Pathao: একই
- [ ] Meta Pixel + Conversions API: Meta Events Manager-এর "Test events"-এ একটা টেস্ট অর্ডার দেখুন
- [ ] ব্লুটুথ প্রিন্টার: আসল প্রিন্টার ও Android অ্যাপে রসিদ ছাপুন (শুধু BLE প্রিন্টার চলবে)
- [ ] অ্যাপ (APK) নতুন করে বিল্ড করে ফোনে ইনস্টল করে লগইন ও একটা বিক্রি

### E. প্রথম দোকান (Zaira's World)
- [ ] পণ্য ও স্টক ঠিকঠাক, একটা আসল POS বিক্রি, একটা অনলাইন অর্ডার, একটা রিটার্ন
- [ ] রাতে ক্যাশ ক্লোজিং করে দেখা, সকালে Dashboard-এর "Needs your attention" দেখা
- [ ] মালিকের ফোনে সন্ধ্যার সারাংশ SMS চালু (Dashboard → Evening summary SMS)
- [ ] CSP রিপোর্ট দেখা: `docker exec zwecosystem-api sh -c 'grep csp-report /data/apps/textlog/* | tail'` — এক সপ্তাহ পরিষ্কার থাকলে nginx-এ `Content-Security-Policy-Report-Only` নাম বদলে `Content-Security-Policy` করুন

---

## ২. Google Drive-এ অফ-সার্ভার ব্যাকআপ (প্রায় ৫ মিনিট)

VPS-এ (একবার):
1. `curl https://rclone.org/install.sh | sudo bash`
2. `rclone config` → `n` (new remote) → নাম `gdrive` → type `drive` (Google Drive) → বাকি সব ডিফল্ট Enter।
   VPS-এ ব্রাউজার নেই, তাই "Use auto config?" প্রশ্নে **n** বলুন। তখন একটা `rclone authorize "drive"` কমান্ড দেখাবে —
   সেটা আপনার **নিজের কম্পিউটারে** চালান (rclone ইনস্টল করে), ব্রাউজারে Google-এ লগইন করুন, যে কোডটা পাবেন VPS-এ পেস্ট করুন।
3. পরীক্ষা: `rclone lsd gdrive:` এবং `rclone copy /data/backups gdrive:auleco-backups --max-age 2d`
4. `/data/apps/zwecosystem/.env`-এ এক লাইন: `BACKUP_REMOTE=gdrive:auleco-backups`
5. রাতের ব্যাকআপ: `crontab -e` → `30 2 * * * /data/apps/zwecosystem/scripts/nightly-backup.sh >> /data/backups/nightly.log 2>&1`
6. একবার হাতে চালান: `bash /data/apps/zwecosystem/scripts/nightly-backup.sh` — Google Drive-এ ফাইল এসেছে কি না দেখুন।

Google Drive-এর ফ্রি জায়গা ১৫GB। স্ক্রিপ্ট ক্লাউডে ৩০ দিনের পুরনো ব্যাকআপ মুছে দেয়। ছবি বেশি হলে (uploads) জায়গা মাঝেমধ্যে দেখুন।

## ৩. HTTPS — কোন পথ নেবেন

আপনি এখনো ঠিক করেননি, তাই তুলনা। দুটোতেই `https://zairasworld.auleco.com`-এর মতো সব দোকানের ঠিকানা কাজ করবে।

| | **Cloudflare (সুপারিশ)** | **Caddy (নিজের সার্ভারে)** | **certbot wildcard** |
|---|---|---|---|
| কাজ | ডোমেইনের DNS Cloudflare-এ নিয়ে যান (ফ্রি), প্রক্সি অন | সার্ভারের সামনে Caddy বসে নিজে সার্টিফিকেট নেয় | DNS API টোকেন দিয়ে `*.auleco.com` সার্টিফিকেট |
| শুরু করা | সবচেয়ে সহজ, সার্ভারে প্রায় কিছুই বদলাতে হয় না | বড় বদল: nginx-এর জায়গায় বসে | মাঝারি |
| নবায়ন | নিজে নিজে | নিজে নিজে | নিজে নিজে (cron) |
| সুবিধা | সাথে DDoS সুরক্ষা ও ক্যাশ | DNS প্রোভাইডার লাগে না | Cloudflare ছাড়া চলে |
| অসুবিধা | আপনার ডোমেইনের নেমসার্ভার বদলাতে হবে | প্রতিটা দোকানের প্রথম ভিজিটে সামান্য দেরি | DNS প্রোভাইডারের API লাগবে |

**Cloudflare-এ গেলে ধাপ:**
1. cloudflare.com-এ ফ্রি অ্যাকাউন্ট → `auleco.com` যোগ → ওরা যে দুটো nameserver দেবে সেটা ডোমেইন রেজিস্ট্রারে বসান।
2. DNS-এ `auleco.com` এবং `*` (wildcard) দুটোই VPS-এর IP-তে, **Proxied (কমলা মেঘ)**।
3. SSL/TLS → **Full** মোড (Flexible না: Flexible-এ Cloudflare থেকে আপনার সার্ভার পর্যন্ত অংশ খোলা থাকে)।
   Full-এর জন্য VPS-এ Cloudflare "Origin Certificate" বসাতে হবে (SSL/TLS → Origin Server → Create Certificate, হোস্ট `auleco.com, *.auleco.com`), ওটা host nginx-এ 443-এ বসবে।
4. "Always Use HTTPS" অন।
5. তারপর নিচের "HTTPS চালুর পর" তালিকা।

আমাকে বলুন কোনটা নিচ্ছেন (এবং Cloudflare হলে আমি host nginx-এর `listen 443 ssl` কনফিগ লিখে দেব)।

**HTTPS চালুর পর:**
1. `admin/src/environments/environment.ts`: `apiUrl` ও `siteUrl` `https://` করুন
2. `capacitor.config.ts`-এর `androidScheme` একই সাথে বদলান এবং APK নতুন করে বানান (অ্যাপ `apiUrl` ভেতরে বহন করে)
3. সব ঠিক চললে `Strict-Transport-Security: max-age=31536000` যোগ করুন (ব্রাউজার এটা মনে রাখে, তাই আগে নিশ্চিত হোন)
4. Meta Pixel ও Google Search Console-এ `https://` ঠিকানা (এরা http আর https আলাদা সাইট ধরে)
5. `bash scripts/smoke-test.sh https://auleco.com zairasworld` — HTTPS অংশে সব সবুজ হওয়া চাই

---

## ৪. রোজকার কাজ ও জরুরি অবস্থা

**ডিপ্লয়:** `main`-এ মার্জ হলে নিজে চলে। আগে সব টেস্ট, তারপর ব্যাকআপ, তারপর ডিপ্লয়, শেষে লাইভ সাইটে স্মোক টেস্ট।
প্রতিটা বিল্ডে image দুটো ট্যাগ পায়: `latest` এবং কমিট আইডি। ডিপ্লয়ের আগে চলমান রিলিজ `:previous` নামে সংরক্ষিত হয়।

**ডিপ্লয়ের পর কিছু ভাঙলে (স্মোক টেস্ট লাল বা দোকানে সমস্যা):**
```
cd /data/apps/zwecosystem
bash scripts/rollback.sh
```
এটা আগের রিলিজ ফিরিয়ে আনে (API + ওয়েবসাইট), ডাটাবেস ছোঁয় না, শেষে API সুস্থ কি না দেখে।
স্কিমা বদল "যোগ করার" ধরনের (`CREATE ... IF NOT EXISTS`), তাই পুরনো কোড সাধারণত নতুন ডাটাবেসে চলে।

**ডাটা নষ্ট হলে / ডাটাবেস ফেরাতে হলে:**
```
bash scripts/restore-db.sh /data/backups/pre-deploy-YYYYMMDD-HHMMSS.sql.gz --live
```
এটা আগে বর্তমান ডাটাবেসের সুরক্ষা-কপি বানায়, `RESTORE` টাইপ করতে বলে, API বন্ধ করে ডাটাবেস বদলায়, আবার চালু করে। **ব্যাকআপের পরের সব তথ্য হারাবে।**

**মাসে একবার ড্রিল (নিরাপদ, লাইভ ছোঁয় না):**
```
bash scripts/restore-db.sh /data/backups/nightly-YYYYMMDD-HHMMSS.sql.gz
```
এটা একটা অস্থায়ী ডাটাবেসে ব্যাকআপ লোড করে মূল টেবিলের সারি গণনা দেখায়, তারপর অস্থায়ী ডাটাবেস মুছে দেয়।

**যেকোনো সময় সব ঠিক আছে কি না:**
```
bash scripts/smoke-test.sh https://auleco.com zairasworld
```
`/api/health` ফ্রি আপটাইম মনিটরেও দিতে পারেন (যেমন UptimeRobot, ৫ মিনিট পরপর): ডাটাবেস না পেলে 503 দেয়।

**ছবি ফেরাতে:** `tar xzf /data/backups/uploads-YYYYMMDD-HHMMSS.tar.gz -C /data/apps`
