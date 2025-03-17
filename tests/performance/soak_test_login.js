import http from 'k6/http';
import { check, sleep } from 'k6';

// Configuration for soak testing
export let options = {
    vus: 100,              // Number of virtual users
    duration: '20m',
    thresholds: {
        http_req_duration: ['p(95)<750'],  // 95% of requests should be under 750ms
        'http_req_failed': ['rate<0.01'],  // Less than 1% of requests should fail
    },
};

export default function () {
    const paramsAuthentication = {
        headers: {
            'Content-Type': 'application/json',
        },
    };

    // API URL for authentication
    const authenticationUrl = 'http://102.33.145.130:5000/api/Account/login';


    // Authentication payload
    const payload = JSON.stringify({
        "username":"prince@initd-it.co.za",
        "password":"3la!ne9Six"
    });

    // Perform authentication
    const resAuth = http.post(authenticationUrl, payload, paramsAuthentication);

    // Check that authentication was successful
    check(resAuth, {
        'authentication succeeded': (r) => r.status === 200,
});

    // Parse the authentication response to extract the token
    const authResponse = JSON.parse(resAuth.body);
    const token = authResponse.token;

    if (token) {

        // Check if the response status is 200 OK
        check(authResponse, {
            'status is 200': (r) => r.status === 200,
    });
    } else {
        console.log('Authentication token not found!');
    }

    sleep(1);
}
