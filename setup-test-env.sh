#!/bin/bash

# ContainerManager.Service - Docker Compose Setup Script
# Sets up test environment and starts services

set -e

# Colors
RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
BLUE='\033[0;34m'
NC='\033[0m'

print_header() {
    echo ""
    echo -e "${BLUE}================================================${NC}"
    echo -e "${BLUE}  $1${NC}"
    echo -e "${BLUE}================================================${NC}"
    echo ""
}

print_info() {
    echo -e "${BLUE}[INFO]${NC} $1"
}

print_success() {
    echo -e "${GREEN}[SUCCESS]${NC} $1"
}

print_warning() {
    echo -e "${YELLOW}[WARNING]${NC} $1"
}

print_error() {
    echo -e "${RED}[ERROR]${NC} $1"
}

# Main setup
main() {
    print_header "ContainerManager.Service Setup"

    # Check if .env exists
    if [ ! -f .env ]; then
        print_warning ".env file not found"
        print_info "Copying .env.example to .env"
        cp .env.example .env
        print_success ".env file created"
        echo ""
        print_warning "Please edit .env file with your actual configuration:"
        echo "  - EMS settings (ServerUrl, Username, Password)"
        echo "  - Azure credentials (SubscriptionId, ResourceGroupName, etc.)"
        echo "  - Queue-to-container mappings"
        echo ""
        read -p "Press Enter after editing .env file..."
    else
        print_success ".env file found"
    fi

    # Build Docker image
    print_header "Building Docker Image"
    print_info "Building container-manager:latest"
    if docker-compose build; then
        print_success "Docker image built successfully"
    else
        print_error "Docker build failed"
        exit 1
    fi

    # Start services
    print_header "Starting Services"
    print_info "Starting ContainerManager.Service with Docker Compose"

    # Ask if user wants test mode
    read -p "Start in test mode with debug logging? (y/n): " test_mode
    if [[ "$test_mode" =~ ^[Yy]$ ]]; then
        print_info "Starting in test mode (debug logging, short timeouts)"
        docker-compose -f docker-compose.yml -f docker-compose.test.yml up -d
    else
        docker-compose up -d
    fi

    print_success "Services started"

    # Wait for health check
    print_header "Waiting for Service"
    print_info "Waiting for service to become healthy (max 60 seconds)..."

    for i in {1..12}; do
        if docker-compose ps | grep -q "healthy"; then
            print_success "Service is healthy"
            break
        fi
        echo -n "."
        sleep 5
    done
    echo ""

    # Show status
    print_header "Service Status"
    docker-compose ps

    # Show logs
    print_header "Recent Logs"
    print_info "Showing last 20 lines of logs:"
    docker-compose logs --tail=20

    # Show instructions
    print_header "Next Steps"
    echo "Service is running! Here are some useful commands:"
    echo ""
    echo "  View logs (live):         docker-compose logs -f"
    echo "  View logs (tail):         docker-compose logs --tail=50"
    echo "  Check status:             docker-compose ps"
    echo "  Stop service:             docker-compose down"
    echo "  Restart service:          docker-compose restart"
    echo "  Rebuild and restart:      docker-compose up -d --build"
    echo ""
    echo "Test scenarios:"
    echo "  See test-data/test-scenarios.md for detailed testing procedures"
    echo ""
    echo "Monitoring:"
    echo "  - Check NOTIFICATION.QUEUE in EMS for notifications"
    echo "  - Monitor Azure Container Apps in Azure Portal"
    echo "  - View service logs for decisions and actions"
    echo ""

    read -p "Follow logs now? (y/n): " follow_logs
    if [[ "$follow_logs" =~ ^[Yy]$ ]]; then
        docker-compose logs -f
    fi
}

# Run main
main