const fs = require('fs');
const http = require('http');

const loginBody = JSON.stringify({ email: 'abc@gmail.com', password: 'password' });

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
    if (res.statusCode !== 200) {
      console.log('Login failed:', res.statusCode, data);
      return;
    }
    const token = JSON.parse(data).token;
    console.log('Token received');
    
    // Now make POST request
    const postBody = JSON.stringify({
      firstName: 'Arif',
      lastName: 'Islam',
      email: 'testuser234@gmail.com',
      phoneNumber: '01854739177',
      role: 'COMPANY_ADMIN',
      companyId: 'fc843a0c-b545-4997-8cf8-3441dd2cac20', 
      isActive: true,
      password: 'password'
    });
    
    const postOptions = {
      hostname: 'localhost',
      port: 5279,
      path: '/api/users',
      method: 'POST',
      headers: {
        'Content-Type': 'application/json',
        'Content-Length': postBody.length,
        'Authorization': 'Bearer ' + token
      }
    };
    
    const postReq = http.request(postOptions, (postRes) => {
      let postData = '';
      postRes.on('data', chunk => postData += chunk);
      postRes.on('end', () => {
        console.log('CreateUser Status:', postRes.statusCode);
        console.log('CreateUser Data:', postData);
      });
    });
    
    postReq.write(postBody);
    postReq.end();
  });
});

req.write(loginBody);
req.end();
