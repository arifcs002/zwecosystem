const http = require('http');

const loginBody = JSON.stringify({ email: 'arifsuperadmin', password: '1514Arif', loginContext: 'admin' });

const options = {
  hostname: 'localhost',
  port: 5279,
  path: '/api/auth/login',
  method: 'POST',
  headers: {
    'Content-Type': 'application/json',
    'Content-Length': Buffer.byteLength(loginBody)
  }
};

const req = http.request(options, (res) => {
  let data = '';
  res.on('data', chunk => data += chunk);
  res.on('end', () => {
    if (res.statusCode !== 200) {
      console.log('Login failed:', res.statusCode, data);
      return;
    }
    const token = JSON.parse(data).token;
    console.log('Token received:', token.substring(0, 20) + '...');
  });
});

req.write(loginBody);
req.end();
