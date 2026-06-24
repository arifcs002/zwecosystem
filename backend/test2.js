const http = require('http');

const loginBody = JSON.stringify({ email: 'abc@gmail.com', password: 'password123' });

const options = {
  hostname: 'localhost',
  port: 5279,
  path: '/api/auth/login',
  method: 'POST',
  headers: {
    'Content-Type': 'application/json',
    'Content-Length': loginBody.length
  }
};

const req = http.request(options, (res) => {
  let data = '';
  res.on('data', chunk => data += chunk);
  res.on('end', () => {
    console.log('Status:', res.statusCode);
    console.log('Data:', data);
  });
});

req.write(loginBody);
req.end();
