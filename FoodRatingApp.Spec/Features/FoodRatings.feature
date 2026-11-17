Feature: Food ratings API integration scenarios
  In order to surface food hygiene ratings to consumers
  As a client of the Food Ratings API
  I want the API to return well-structured rating and authority data
  So that downstream systems can reliably consume and display it

# Stub data: authority 1 is English (FHRS), authority 2 is Scottish (FHIS), any other id is unknown.

@integration
Scenario Outline: Ratings response returns the correct rating and percentage value
  Given the API endpoint is "<endpoint>"
  When the API is called
  Then the response HTTP status code should be 200 OK
  And the response should contain "<rating>"
  And the response should contain "<percentage>"

Examples:
  | endpoint                     | rating               | percentage |
  | https://localhost:5001/api/1 | 5                    | 50         |
  | https://localhost:5001/api/1 | Exempt               | 30         |
  | https://localhost:5001/api/2 | Pass and Eat Safe    | 50         |
  | https://localhost:5001/api/2 | Improvement Required | 35         |

@integration
Scenario: Authority list response includes an id and name for each authority
  Given the API endpoint is "https://localhost:5001/api"
  When the API is called
  Then the response HTTP status code should be 200 OK
  And the response body should contain JSON field "id"
  And the response body should contain JSON field "name"

@integration
Scenario: Request with a non-integer authority id returns 400 Bad Request
  Given the API endpoint is "https://localhost:5001/api/invalid"
  When the API is called
  Then the response HTTP status code should be 400 Bad Request

@integration
Scenario: Request for an unknown authority returns 404 Not Found
  Given the API endpoint is "https://localhost:5001/api/999"
  When the API is called
  Then the response HTTP status code should be 404 Not Found

@integration
Scenario Outline: Failures of the FSA API are reported with a meaningful status code
  Given the API endpoint is "https://localhost:5001/api/<authorityId>"
  When the API is called
  Then the response HTTP status code should be <status>
  And the response should contain "<title>"
  And the response should not contain "Exception"

Examples:
  | authorityId | status                  | title                                      |
  | 502         | 502 Bad Gateway         | Invalid response from food ratings service |
  | 503         | 503 Service Unavailable | Food ratings service unavailable           |
  | 504         | 504 Gateway Timeout     | Food ratings service timed out             |

@integration
Scenario: Authorities are lazy loaded one page at a time
  Given the API endpoint is "https://localhost:5001/api?page=2&pageSize=1"
  When the API is called
  Then the response HTTP status code should be 200 OK
  And the response should contain "Authority 2"
  And the response should not contain "Authority 1"
  And the response body should contain JSON field "id"
