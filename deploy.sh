#!/bin/bash
scp docker-compose.production.yml 10.0.0.1:zonnewijzer-finance/
ssh 10.0.0.1 "cd zonnewijzer-finance; ./update-and-restart.sh"