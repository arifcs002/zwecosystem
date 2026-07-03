import type { CapacitorConfig } from '@capacitor/cli';

const config: CapacitorConfig = {
  appId: 'com.zwecosystem.app',
  appName: 'ZW Ecosystem',
  webDir: 'dist/admin',
  // The backend is plain HTTP (http://194.5.152.74:85). Capacitor's WebView
  // defaults to an https://localhost origin, which makes every call to that
  // http API a *mixed-content* request — Android's WebView blocks those even
  // after cleartext traffic is permitted, surfacing as "0 Unknown Error".
  // Loading the app from an http://localhost origin removes the mixed-content
  // mismatch entirely; allowMixedContent is a belt-and-suspenders backup.
  server: {
    androidScheme: 'http'
  },
  android: {
    allowMixedContent: true
  }
};

export default config;
