Feature: Authority ratings response contract
  In order to display accurate food hygiene rating distributions for a given authority
  As a client of the Food Ratings API
  I want the GET /api/{authorityId} endpoint to return a complete set of rating categories
  So that the values are structurally valid and always sum to 100 percent

# Stub data: authority 2 is Scottish (FHIS); every other id is English (FHRS).

@contract
Scenario Outline: Ratings response contains every category in the authority's scheme
    Given the API endpoint is "https://localhost:5001/api/<authorityId>"
    When the API is called
    Then the response should contain <count> rating items

Examples:
    | authorityId | count |
    | 1           | 6     |
    | 999         | 6     |
    | 2           | 3     |

@contract
Scenario Outline: All rating values sum to 100
    Given the API endpoint is "https://localhost:5001/api/<authorityId>"
    When the API is called
    Then the sum of all rating values should equal 100.0

Examples:
    | authorityId |
    | 1           |
    | 999         |
    | 2           |

@contract
Scenario: FHRS ratings are returned in descending numerical order, then Exempt (as in the user story example)
    Given the API endpoint is "https://localhost:5001/api/1"
    When the API is called
    Then the ratings should include the following categories:
      | category |
      | 5        |
      | 4        |
      | 3        |
      | 2        |
      | 1        |
      | Exempt   |

@contract
Scenario: FHIS ratings are returned in the required order
    Given the API endpoint is "https://localhost:5001/api/2"
    When the API is called
    Then the ratings should include the following categories:
      | category             |
      | Pass and Eat Safe    |
      | Pass                 |
      | Improvement Required |

@contract
Scenario: Response content type is application/json
    Given the API endpoint is "https://localhost:5001/api/1"
    When the API is called
    Then the response content type should be "application/json"
